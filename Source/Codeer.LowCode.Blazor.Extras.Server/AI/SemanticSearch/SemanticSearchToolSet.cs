using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField を置いたモジュールの行を「意味で探す」ツール (<c>search_records</c>)。
    /// 質問文を埋め込みにして、索引 (行を文章にしたものとそのベクトル) とのコサイン類似度が高い行を Id・詳細 URL・文章つきで返す。
    /// SQL の集計 (RawDataAccessToolSet) と役割を分け、「似た事例」「〜のような問い合わせ」のように内容で探す質問に使わせる。
    /// 読める範囲は RawDataAccess と同じ (データソース名で絞る。行ごとの閲覧条件は効かない)。
    /// </summary>
    internal sealed class SemanticSearchToolSet : IAIChatToolSet
    {
        readonly Func<DesignData?> _design;
        readonly Func<IDbAccessor> _dbAccessorFactory;
        readonly Func<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGeneratorFactory;
        readonly IList<string> _dataSourceNames;
        readonly int _maxTextChars;
        readonly int _maxTop;

        /// <param name="dataSourceNames">検索を許すデータソース名 (RawDataAccessOptions.DataSourceNames)。空なら全モジュール</param>
        /// <param name="maxTextChars">1 件の文章を AI に返す最大文字数</param>
        /// <param name="maxTop">1 回に返す件数の上限</param>
        public SemanticSearchToolSet(Func<DesignData?> design, Func<IDbAccessor> dbAccessorFactory, Func<IEmbeddingGenerator<string, Embedding<float>>> embeddingGeneratorFactory,
            IList<string> dataSourceNames, int maxTextChars = 1500, int maxTop = 20)
        {
            _design = design;
            _dbAccessorFactory = dbAccessorFactory;
            _embeddingGeneratorFactory = embeddingGeneratorFactory;
            _dataSourceNames = dataSourceNames;
            _maxTextChars = maxTextChars;
            _maxTop = maxTop;
        }

        public string GetInstructions(AIChatToolContext context)
        {
            var design = _design();
            var targets = design == null ? new() : SearchableModules(design);
            if (targets.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("次のモジュールは意味検索 (search_records) で「内容が似た記録」を探せます (各行を文章にして埋め込みで索引済み):");
            foreach (var (module, field) in targets)
            {
                var title = DesignDescriber.Title(design!, module);
                sb.Append("- ").Append(module.Name);
                if (!string.IsNullOrEmpty(title) && title != module.Name) sb.Append(" [").Append(title).Append(']');
                sb.Append(" (文章にしているフィールド: ").Append(string.Join(", ", SemanticSearchText.SourceFields(module, field).Select(n => FieldLabel(module, n)))).AppendLine(")");
            }
            sb.AppendLine("- 「似た事例は」「〜のような問い合わせ」「〜について書かれた記録」のように内容や意味で探す質問は、SQL の LIKE ではなく search_records を使ってください。件数や合計などの集計は SQL です。");
            sb.AppendLine("- search_records の結果には Id・詳細ページの URL・文章が付きます。行を挙げるときは URL で Markdown リンクを付け、score が低いものは「近いものは見つからなかった」と正直に伝えてください。");
            return sb.ToString();
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            var design = _design();
            if (design == null || SearchableModules(design).Count == 0) yield break;
            yield return AIFunctionFactory.Create(
                ([Description("探すモジュール名 (意味検索できるモジュールの一覧にある名前)。")] string moduleName,
                 [Description("探したい内容 (自然文でよい。ユーザーの言葉をそのまま、または要点を補って)。")] string query,
                 [Description("返す件数 (既定 5)。")] int top = 5)
                    => SearchAsync(moduleName, query, top, context),
                "search_records",
                "モジュールの行を内容の意味で探し、似ている順に Id・score (0〜1)・詳細 URL・文章を返す。");
        }

        async Task<string> SearchAsync(string moduleName, string query, int top, AIChatToolContext context)
        {
            var design = _design();
            var searchable = design == null ? new() : SearchableModules(design);
            var found = searchable.Where(t => string.Equals(t.Module.Name, (moduleName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (design == null || found.Count == 0)
                return Json(new { error = $"モジュール '{moduleName}' は意味検索できません。使えるのは: {string.Join(", ", searchable.Select(t => t.Module.Name))}" });
            var target = found[0];
            if (string.IsNullOrWhiteSpace(query)) return Json(new { error = "query が空です。" });
            top = Math.Clamp(top, 1, _maxTop);

            context.Progress.Report(Resources.AIChat_SearchingRecords);
            context.Logger?.LogInformation("AIChat search_records {Module} by {User}: {Query}", target.Module.Name, context.Request.UserName, query);
            try
            {
                var generator = _embeddingGeneratorFactory();
                var embeddings = await generator.GenerateAsync(new[] { query }, cancellationToken: context.CancellationToken);
                var queryVector = embeddings[0].Vector.ToArray();

                List<SemanticSearchIndexReader.Entry> entries;
                await using (var db = _dbAccessorFactory())
                    entries = await SemanticSearchIndexReader.ReadAsync(db, target.Module, target.Field, context.CancellationToken);

                var urls = DesignDescriber.PageUrls(design, target.Module);
                var results = entries
                    .Select(e => (Entry: e, Score: SemanticSearchVector.Cosine(queryVector, e.Vector)))
                    .OrderByDescending(x => x.Score)
                    .Take(top)
                    .Select(x => new
                    {
                        id = x.Entry.Id,
                        score = Math.Round(x.Score, 3),
                        url = urls?.Detail.Replace("{Id}", x.Entry.Id),
                        text = x.Entry.Text.Length > _maxTextChars ? x.Entry.Text[.._maxTextChars] + "…" : x.Entry.Text,
                    })
                    .ToList();
                return Json(new { module = target.Module.Name, indexedCount = entries.Count, results });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                context.Logger?.LogWarning(e, "AIChat search_records failed for {Module}", target.Module.Name);
                return Json(new { error = e.Message });
            }
        }

        //意味検索できるモジュール = SemanticSearchField (両列あり) を持ち、表があり、許されたデータソースのもの
        List<(ModuleDesign Module, SemanticSearchFieldDesign Field)> SearchableModules(DesignData design)
        {
            var result = new List<(ModuleDesign, SemanticSearchFieldDesign)>();
            foreach (var name in design.Modules.GetModuleNames())
            {
                var module = design.Modules.Find(name);
                if (module == null || string.IsNullOrEmpty(module.DbTable)) continue;
                if (_dataSourceNames.Count > 0 && !_dataSourceNames.Contains(module.DataSourceName, StringComparer.OrdinalIgnoreCase)) continue;
                var field = module.Fields.OfType<SemanticSearchFieldDesign>().FirstOrDefault(f => f.HasColumns);
                if (field != null) result.Add((module, field));
            }
            return result;
        }

        static string FieldLabel(ModuleDesign module, string name)
        {
            var display = (module.Fields.FirstOrDefault(f => f.Name == name) as ValueFieldDesignBase)?.DisplayName;
            return string.IsNullOrEmpty(display) ? name : display;
        }

        static readonly JsonSerializerOptions _json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        static string Json(object value) => JsonSerializer.Serialize(value, _json);
    }
}
