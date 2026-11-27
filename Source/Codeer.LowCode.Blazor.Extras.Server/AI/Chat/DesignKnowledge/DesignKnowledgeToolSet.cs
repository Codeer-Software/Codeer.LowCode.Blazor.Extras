using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge
{
    /// <summary>
    /// アプリの設計 (デザインプロジェクト) を AI に読ませるツール群。業務の意味を持っているのは DB スキーマではなくデザインなので、
    /// モデルには「モジュールと業務語で考え、表と列は describe_module で確かめる」よう促す。
    /// <c>list_modules</c> (一覧) / <c>describe_module</c> (フィールド・候補値・リンク・スクリプト) /
    /// <c>read_document</c> (デザインプロジェクトに置いた補足の文書。<see cref="AIChatDocument"/>。どのフォルダを渡すかは AIChatField のデザインの
    /// DocumentFolder で決まり、チャットごとに違う文書の組を選べる。合計が小さければ全文をプロンプトに入れ、大きければ一覧と抜粋だけ入れてツールで読ませる)。
    /// デザインはホットリロードで変わるので Func で都度取る。ユーザーごとのデザイン (そのユーザーへ画面用に送るもの) を渡す Agent は、
    /// ツールの文脈 (依頼したユーザー) から取る形で渡す。1 回の返事の中では最初に取ったものを使い回す。
    /// </summary>
    internal sealed class DesignKnowledgeToolSet : IAIChatToolSet
    {
        readonly Func<AIChatToolContext, Task<DesignData?>>? _design;
        readonly Func<string, IReadOnlyList<AIChatDocument>>? _documents;
        readonly int _maxScriptChars;
        readonly int _inlineDocumentsMaxChars;
        readonly int _excerptChars;
        readonly bool _showDbNames;
        readonly Func<ModuleDesign, AIChatToolContext, bool>? _isVisible;

        /// <param name="inlineDocumentsMaxChars">文書の合計がこの文字数以下なら全文をシステムプロンプトに入れる (超えたら一覧 + 抜粋 + read_document)</param>
        /// <param name="documents">文書フォルダ (AIChatField のデザインの DocumentFolder) → 文書の列。フォルダが空のときは呼ばれない</param>
        /// <param name="showDbNames">DB の名前 (データソース・表・列) と SQL を書くための注意を出すか。SQL を書く Agent だけ true (<see cref="DesignDescriber"/>)</param>
        /// <param name="isVisible">設計として見せるモジュールの絞り (null なら全部)。見せないモジュールは「無い」のと同じ扱い</param>
        public DesignKnowledgeToolSet(Func<DesignData?>? design, Func<string, IReadOnlyList<AIChatDocument>>? documents, bool showDbNames,
            Func<ModuleDesign, AIChatToolContext, bool>? isVisible = null, int maxScriptChars = 6000, int inlineDocumentsMaxChars = 8000, int excerptChars = 200)
            : this(design == null ? null : _ => Task.FromResult(design()), documents, showDbNames, isVisible, maxScriptChars, inlineDocumentsMaxChars, excerptChars)
        {
        }

        /// <param name="design">ツールの文脈 (依頼したユーザー) からデザインを取る。ユーザーごとに違うデザインを見せるときに使う</param>
        public DesignKnowledgeToolSet(Func<AIChatToolContext, Task<DesignData?>>? design, Func<string, IReadOnlyList<AIChatDocument>>? documents, bool showDbNames,
            Func<ModuleDesign, AIChatToolContext, bool>? isVisible = null, int maxScriptChars = 6000, int inlineDocumentsMaxChars = 8000, int excerptChars = 200)
        {
            _showDbNames = showDbNames;
            _isVisible = isVisible;
            _design = design;
            _documents = documents;
            _maxScriptChars = maxScriptChars;
            _inlineDocumentsMaxChars = inlineDocumentsMaxChars;
            _excerptChars = excerptChars;
        }

        public string GetInstructions(AIChatToolContext context)
        {
            {
                var sb = new StringBuilder();
                if (_design != null)
                {
                    sb.AppendLine("このアプリの設計 (モジュール = 画面とデータの単位) を参照できます。業務の意味は DB スキーマではなく設計にあります。");
                    sb.AppendLine("- 質問に出てくる業務語 (受注、得意先、状態など) は、まず list_modules / describe_module でどのモジュール・フィールド・候補値に当たるかを確かめてください。");
                    if (_showDbNames)
                    {
                        sb.AppendLine("- describe_module には表と DB 列、候補値 (コード=名称)、リンク (どの表とどのキーで結ぶか)、論理削除、スクリプトが出ます。SQL はこれに基づいて書き、これで表と列が分かるなら DB スキーマ (get_schema) は読まないでください。");
                        sb.AppendLine("- 論理削除の列がある表は、削除済みの行を除いて集計してください。");
                        sb.AppendLine("- 個々の行 (伝票や案件など) を挙げるときは、describe_module の「画面 URL」を使って詳細ページへの Markdown リンクを付けてください (例: [開く](/Main/Order/123))。{Id} には SQL で一緒に取った Id 列の値を入れます。集計値だけの答えにはリンクは要りません。");
                    }
                    else
                    {
                        sb.AppendLine("- describe_module にはフィールド (名前・表示名・型)、候補値 (コード=名称)、リンク (どのモジュールのどの項目を指すか)、スクリプトが出ます。");
                        sb.AppendLine("- 個々の行 (伝票や案件など) を挙げるときは、describe_module の「画面 URL」を使って詳細ページへの Markdown リンクを付けてください (例: [開く](/Main/Order/123))。{Id} にはレコードの Id を入れます。集計値だけの答えにはリンクは要りません。");
                    }
                }
                var documents = SafeDocuments(context);
                if (documents.Count > 0)
                {
                    sb.AppendLine("補足文書には、この業務での用語の定義 (「売上」は何を指すか等) や集計の決まりが書いてあります。文書に定義がある語はその定義に従い、自分の解釈で置き換えないでください。");
                    if (documents.Sum(d => d.Text.Length) <= _inlineDocumentsMaxChars)
                    {
                        //小さければ全文をプロンプトに入れる (読み忘れが起きない)
                        foreach (var document in documents)
                            sb.AppendLine().Append("### 文書: ").AppendLine(document.Name).AppendLine(document.Text.Trim());
                    }
                    else
                    {
                        sb.AppendLine("文書の一覧 (冒頭の抜粋)。関係しそうなものは read_document で全文を読んでから答えてください:");
                        foreach (var document in documents)
                        {
                            var excerpt = string.Join(" ", document.Text.Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).Where(l => l.Length > 0));
                            sb.Append("- ").Append(document.Name).Append(": ").AppendLine(excerpt.Length > _excerptChars ? excerpt[.._excerptChars] + "…" : excerpt);
                        }
                    }
                }
                return sb.ToString();
            }
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            if (_design != null)
            {
                //1 回の返事の中では最初に取ったデザインを使い回す (ユーザーごとのデザインは作るのに手間が掛かる)
                Task<DesignData?>? loaded = null;
                Func<Task<DesignData?>> design = () => loaded ??= LoadDesignAsync(context);
                yield return AIFunctionFactory.Create(
                    () => ListModulesAsync(design, context),
                    "list_modules",
                    _showDbNames ? "アプリのモジュール (画面 / データの単位) の一覧を返す: 名前、表とデータソース、リンク先。" : "アプリのモジュール (画面 / データの単位) の一覧を返す: 名前、リンク先。");
                yield return AIFunctionFactory.Create(
                    ([Description("モジュール名 (list_modules の名前)。")] string moduleName) => DescribeModuleAsync(design, moduleName, context),
                    "describe_module",
                    _showDbNames ? "モジュールの詳細を返す: フィールド (表示名・型・DB 列・候補値)、リンク (結合相手とキー)、論理削除、スクリプト。" : "モジュールの詳細を返す: フィールド (表示名・型・候補値)、リンク (相手のモジュールと項目)、スクリプト。");
            }
            if (_documents != null && SafeDocuments(context).Count > 0)
            {
                yield return AIFunctionFactory.Create(
                    ([Description("文書名 (一覧にある名前)。")] string name) => ReadDocument(name, context),
                    "read_document",
                    "補足文書の本文を返す (用語の定義、集計の決まり、データの見方など)。");
            }
        }

        async Task<DesignData?> LoadDesignAsync(AIChatToolContext context)
        {
            try { return await _design!(context); }
            catch (Exception e)
            {
                context.Logger?.LogWarning(e, "AIChat design load failed for {User}", context.Request.UserName);
                return null;
            }
        }

        async Task<string> ListModulesAsync(Func<Task<DesignData?>> load, AIChatToolContext context)
        {
            context.Progress.Report(Resources.AIChat_ReadingDesign);
            var design = await load();
            if (design == null) return "(設計が読めません)";
            context.Logger?.LogInformation("AIChat design list_modules by {User}", context.Request.UserName);
            return DesignDescriber.ListModules(design, _showDbNames, IsVisible(context));
        }

        async Task<string> DescribeModuleAsync(Func<Task<DesignData?>> load, string moduleName, AIChatToolContext context)
        {
            context.Progress.Report(Resources.AIChat_ReadingDesign);
            var design = await load();
            if (design == null) return "(設計が読めません)";
            context.Logger?.LogInformation("AIChat design describe_module {Module} by {User}", moduleName, context.Request.UserName);
            return DesignDescriber.DescribeModule(design, moduleName ?? string.Empty, _maxScriptChars, _showDbNames, IsVisible(context))
                ?? $"モジュール '{moduleName}' はありません。list_modules で名前を確かめてください。";
        }

        Func<ModuleDesign, bool>? IsVisible(AIChatToolContext context)
            => _isVisible == null ? null : module => _isVisible(module, context);

        string ReadDocument(string name, AIChatToolContext context)
        {
            context.Progress.Report(Resources.AIChat_ReadingDocument);
            var documents = SafeDocuments(context);
            var document = documents.FirstOrDefault(d => string.Equals(d.Name, (name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            context.Logger?.LogInformation("AIChat read_document {Document} by {User}: {Found}", name, context.Request.UserName, document != null);
            return document?.Text ?? $"文書 '{name}' はありません。あるのは: {string.Join(", ", documents.Select(d => d.Name))}";
        }

        //文書はデザインの DocumentFolder で選ぶ。空 = 文書なし (トークンを使わない)
        IReadOnlyList<AIChatDocument> SafeDocuments(AIChatToolContext context)
        {
            var folder = (context.Request.DocumentFolder ?? string.Empty).Trim().Trim('/', '\\');
            if (_documents == null || folder.Length == 0) return Array.Empty<AIChatDocument>();
            try { return _documents(folder) ?? Array.Empty<AIChatDocument>(); }
            catch { return Array.Empty<AIChatDocument>(); }
        }
    }
}
