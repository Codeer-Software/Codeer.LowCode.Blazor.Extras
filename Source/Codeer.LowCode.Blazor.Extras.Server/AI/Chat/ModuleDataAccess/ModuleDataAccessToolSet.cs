using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// AI にアプリのレコードを「実行ユーザーの権限で」読ませるツール群: <c>find_records</c> (条件・並び・件数を指定して行を返す)、
    /// <c>aggregate_records</c> (本体の集計 API <see cref="ModuleDataIO.AggregateAsync"/> で DB 側に GROUP BY させる)、
    /// <c>cross_tab</c> (行 × 列のクロス表。<see cref="CrossTabBuilder"/> で合計も別クエリで取る)。どれも本体の ModuleDataIO を通るので、
    /// モジュールの閲覧権限 (UserRead)・行の条件 (DataRead)・項目の読み取り権限 (PermissionField)・論理削除・アプリアクセス条件が画面と同じに効く。
    /// SQL は書かせない (生 SQL で DB を読むのは <see cref="RawDataAccess.RawDataAccessToolSet"/>)。
    /// <para>
    /// 行を読む ModuleDataIO はツール呼び出しごとに <see cref="ModuleDataAccessScope"/> として開いて閉じる (バックグラウンド実行のためリクエストの寿命に乗れない)。
    /// 誰の権限で開くかは依頼のユーザー (<see cref="AIChatAgentRequest.UserName"/> = AIChatService.StartAsync の ownerKey) で決まる。
    /// </para>
    /// </summary>
    internal sealed class ModuleDataAccessToolSet : IAIChatToolSet
    {
        readonly Func<string, Task<ModuleDataAccessScope>> _openScope;
        readonly Func<DesignData?> _design;
        readonly ModuleDataAccessOptions _options;
        readonly Func<IEmbeddingProvider?>? _embeddingProvider;

        /// <param name="openScope">ユーザー Id → そのユーザーの権限を持つ ModuleDataIO (ツール呼び出しごとに開いて閉じる)</param>
        /// <param name="design">デザイン定義 (ホットリロードで変わるので都度取る)。項目の型と候補値の解決に使う</param>
        /// <param name="embeddingProvider">意味検索 (search_records) の質問を埋め込むプロバイダ (索引付けと同じもの)。null か、返すものが null なら意味検索は付かない</param>
        public ModuleDataAccessToolSet(Func<string, Task<ModuleDataAccessScope>> openScope, Func<DesignData?> design, ModuleDataAccessOptions options,
            Func<IEmbeddingProvider?>? embeddingProvider = null)
        {
            _openScope = openScope;
            _design = design;
            _options = options;
            _embeddingProvider = embeddingProvider;
        }

        //意味検索できるモジュール: 表を持ち、文章・ベクトル・ベクトル検索の列が揃った SemanticSearchField があるもの (DB がベクトル検索に対応するかは実行時に分かる)
        static List<(ModuleDesign Module, SemanticSearchFieldDesign Field)> SemanticModules(DesignData design)
            => design.Modules.GetModuleNames().Select(design.Modules.Find)
                .Where(m => m != null && !string.IsNullOrEmpty(m.DbTable))
                .SelectMany(m => m!.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns).Take(1).Select(f => (m!, f)))
                .ToList();

        IEmbeddingProvider? Provider() => _embeddingProvider?.Invoke();

        /// <summary>
        /// 絞り込み条件 1 つ。項目の比較 (Field / Comparison / Value) か、条件のグループ (All = すべて満たす / Any = どれかを満たす) のどちらか。
        /// グループは入れ子にでき、Not で否定する。本体の MultiMatchCondition (AND / OR / NOT の木) にそのまま落ちる。
        /// </summary>
        internal sealed class Filter
        {
            [Description("項目名 (describe_module のフィールド名)。リンク先の項目は describe_module に出るリンク越しの項目名 (例: Customer.Name)。グループ (All / Any) のときは省略。")]
            public string? Field { get; set; }
            [Description("Equal / NotEqual / LessThan / LessThanOrEqual / GreaterThan / GreaterThanOrEqual / Like (部分一致) / In / NotIn。省略は Equal。")]
            public string? Comparison { get; set; }
            [Description("比較する値。候補値のある項目はコード (候補値の左側)。日付は yyyy-MM-dd。In / NotIn は配列。")]
            public JsonElement Value { get; set; }
            [Description("すべて満たす条件のグループ (AND)。入れ子にできる。Any と同時には指定しない。")]
            public Filter[]? All { get; set; }
            [Description("どれかを満たす条件のグループ (OR)。入れ子にできる。All と同時には指定しない。")]
            public Filter[]? Any { get; set; }
            [Description("true ならこの条件 (またはグループ) を否定する。")]
            public bool Not { get; set; }
        }

        /// <summary>並び順 1 つ。</summary>
        internal sealed class Sort
        {
            [Description("項目名。")]
            public string Field { get; set; } = string.Empty;
            [Description("true なら降順。")]
            public bool Descending { get; set; }
        }

        /// <summary>aggregate_records / cross_tab のグループ化の項目 (本体の AggregateGroup: 値そのまま、日付は単位でまとめる)。</summary>
        internal sealed class GroupBy
        {
            [Description("項目名。")]
            public string Field { get; set; } = string.Empty;
            [Description("日付・日時の項目をまとめる単位: year / quarter / month / week / day / hour。それ以外の項目では省略。")]
            public string? DateUnit { get; set; }
            [Description("year / quarter のときの年度の開始月 (1〜12)。省略すると設定の既定 (通常 1 = 暦年)。")]
            public int? FiscalYearStartMonth { get; set; }
        }

        /// <summary>集計値 (本体の AggregateMeasure)。</summary>
        internal sealed class Measure
        {
            [Description("count (件数) / countDistinct (値の種類数) / sum / avg / min / max。")]
            public string Function { get; set; } = "count";
            [Description("集計する項目の名前。count では省略。sum / avg は数値項目、min / max は数値・日付・文字でも可。")]
            public string? Field { get; set; }
        }

        /// <summary>集計後の絞り込み (HAVING 相当。本体の AggregateHaving)。</summary>
        internal sealed class Having
        {
            [Description("measures の何番目の値か (0 始まり)。")]
            public int MeasureIndex { get; set; }
            [Description("Equal / NotEqual / LessThan / LessThanOrEqual / GreaterThan / GreaterThanOrEqual。")]
            public string? Comparison { get; set; }
            [Description("比較する数値。")]
            public decimal Value { get; set; }
        }

        /// <summary>集計結果の並び (本体の AggregateSort)。</summary>
        internal sealed class AggregateOrder
        {
            [Description("group (グループ化した項目) か measure (集計値)。")]
            public string Target { get; set; } = "group";
            [Description("groupBy または measures の何番目か (0 始まり)。")]
            public int Index { get; set; }
            [Description("true なら降順。")]
            public bool Descending { get; set; }
        }

        public string GetInstructions(AIChatToolContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("アプリのレコードを読むことができます (find_records / aggregate_records / cross_tab)。読めるのは、今のユーザーがアプリの画面で見られるレコードと項目だけです (モジュールの閲覧権限・行の条件・項目の読み取り権限が効きます。削除済みの行は出ません)。");
            sb.AppendLine("- 権限が無いモジュールや項目を読むとエラー (error と accessDenied=true) になります。そのときは読めなかったことをユーザーに伝えてください。別の経路で読もうとしないこと。");
            sb.AppendLine("- 絞り込みは filters に項目の比較 ({ field, comparison, value }) を並べます (すべて満たす = AND。matchAny で OR)。AND と OR を組み合わせるときは、条件の代わりにグループ ({ all: [...] } / { any: [...] }) を置きます (入れ子可)。not: true で否定できます。");
            sb.AppendLine("- 手順: まず補足文書と describe_module で項目名・候補値・リンクを確かめ、それから find_records / aggregate_records / cross_tab を呼びます。項目は describe_module に出るフィールド名で指定します (DB の列名ではありません)。リンク先の項目は、describe_module にリンク越しの項目 (例: Customer.Name) として出ているものだけ使えます。SQL は書けません。");
            sb.AppendLine("- 候補値のある項目 (状態など) は、条件にコード (候補値の左側) を使います。結果では { value, text } の形で値と表示名の両方が返ります。");
            sb.AppendLine("- 件数・合計・平均・最小・最大・値の種類数は aggregate_records で求めてください (DB が計算します)。行を取って自分で足し算しないこと。groupBy で項目ごとに分け、日付は year / quarter / month / week / day / hour でまとめられます。having で集計後の絞り込み (合計が N 以上など)、sort と limit で上位 N 件 (例: 売上の多い得意先 10 件 = sort [{ target: measure, index: 0, descending: true }], limit 10)。");
            sb.AppendLine($"- 行と列の 2 方向で見たい表 (担当者 × 月など) は cross_tab で求めてください。行と列の見出し・セル・行ごとの合計・列ごとの合計・総計が揃って返るので、そのまま Markdown の表にします (合計は DB で別に集計した正しい値です。セルを足して作らないこと)。表のセル数の上限は {_options.MaxCrossTabCells} です。");
            sb.AppendLine($"- find_records は 1 回に最大 {_options.MaxRows} 行です。fields で必要な項目だけに絞ると結果が小さくなります。続きは page を進めて取ります (totalCount と pageCount が返ります)。");
            sb.AppendLine($"- aggregate_records は 1 回に最大 {_options.MaxGroups} グループです (limited=true なら続きがあります。並びを指定して必要な分だけ取ってください)。");
            var design = _design();
            var semantic = design == null || Provider() == null ? new() : SemanticModules(design);
            if (semantic.Count > 0)
            {
                sb.AppendLine($"- 次のモジュールは search_records で内容の意味で探せます (似ている順。filters で普通の条件と組み合わせられます): {string.Join(", ", semantic.Select(e => e.Module.Name))}。「似た事例」「〜のような問い合わせ」のように内容で探す質問は Like ではなく search_records を使ってください。結果は近い順に並んでいますが、本当に関係があるかは中身を読んで判断してください。");
            }
            sb.AppendLine("- 1 件の内容を子一覧 (明細など) ごと見たいときは get_record (モジュール名と Id) を使います。find_records は子一覧を返しません。");
            if (_options.CommandTimeoutSeconds > 0)
                sb.AppendLine($"- 1 回の読み取りは {_options.CommandTimeoutSeconds} 秒で打ち切られます。時間切れになったら同じ指定を繰り返さず、条件で絞るか、日付の単位を大きくしてください。");
            sb.AppendLine("- 個々の行を挙げるときは describe_module の「画面 URL」と行の Id で詳細ページへの Markdown リンクを付けられます。");
            sb.AppendLine("- 結果は Markdown の表で示し、続けて短い解釈を書いてください。数値はツールの結果に基づくもの以外を書かないこと。");
            if (!string.IsNullOrWhiteSpace(_options.AdditionalInstructions)) sb.AppendLine().Append(_options.AdditionalInstructions.Trim());
            return sb.ToString();
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            yield return AIFunctionFactory.Create(
                ([Description("モジュール名 (list_modules の名前)。")] string moduleName,
                 [Description("このツール呼び出しで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose,
                 [Description("絞り込み条件 (すべて満たす行)。項目の比較か、グループ (all / any。入れ子可)。省略で全行。")] Filter[]? filters = null,
                 [Description("true なら filters のどれかを満たす行 (OR)。")] bool matchAny = false,
                 [Description("並び順 (先頭が優先)。")] Sort[]? sort = null,
                 [Description("返す項目名 (省略で全項目。Id は常に返る)。")] string[]? fields = null,
                 [Description("1 ページの行数 (上限あり)。")] int? limit = null,
                 [Description("ページ番号 (0 始まり)。")] int page = 0)
                    => FindRecordsAsync(moduleName, purpose, filters, matchAny, sort, fields, limit, page, context),
                "find_records",
                "モジュールのレコードを、今のユーザーの権限で読んで返す (条件・並び・項目・ページ指定)。結果は rows (項目名 → 値。選択・リンクは { value, text }) と totalCount / pageCount。");
            yield return AIFunctionFactory.Create(
                ([Description("モジュール名 (list_modules の名前)。")] string moduleName,
                 [Description("このツール呼び出しで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose,
                 [Description("レコードの Id。")] string id)
                    => GetRecordAsync(moduleName, purpose, id, context),
                "get_record",
                "Id で 1 件のレコードを、今のユーザーの権限で子一覧 (ListField / ModuleField) ごと読んで返す。結果は row (項目名 → 値) と children (子一覧名 → { module, rowCount, rows })。");
            var design = _design();
            if (design != null && Provider() != null && SemanticModules(design).Count > 0)
            {
                yield return AIFunctionFactory.Create(
                    ([Description("探すモジュール名 (意味検索できるモジュールの一覧にある名前)。")] string moduleName,
                     [Description("このツール呼び出しで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose,
                     [Description("探したい内容 (自然文でよい。ユーザーの言葉をそのまま、または要点を補って)。")] string query,
                     [Description("返す件数 (既定 5。上限あり)。")] int top = 5,
                     [Description("一緒に掛ける絞り込み条件 (すべて満たす行)。find_records と同じ書き方。省略可。")] Filter[]? filters = null,
                     [Description("true なら filters のどれかを満たす行 (OR)。")] bool matchAny = false,
                     [Description("返す項目名 (省略で全項目。Id は常に返る)。")] string[]? fields = null)
                        => SearchRecordsAsync(moduleName, purpose, query, top, filters, matchAny, fields, context),
                    "search_records",
                    "モジュールの行を、今のユーザーの権限で内容の意味で探し、似ている順に返す (DB のベクトル検索)。結果は rows (find_records と同じ形) を近い順に。");
            }
            yield return AIFunctionFactory.Create(
                ([Description("モジュール名 (list_modules の名前)。")] string moduleName,
                 [Description("このツール呼び出しで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose,
                 [Description("集計値 (1 つ以上)。")] Measure[] measures,
                 [Description("絞り込み条件 (すべて満たす行)。項目の比較か、グループ (all / any。入れ子可)。省略で全行。")] Filter[]? filters = null,
                 [Description("true なら filters のどれかを満たす行 (OR)。")] bool matchAny = false,
                 [Description("グループ化する項目 (省略で全体を 1 グループ)。")] GroupBy[]? groupBy = null,
                 [Description("集計後の絞り込み (全部を満たすグループだけ)。")] Having[]? having = null,
                 [Description("並び順 (先頭が優先)。省略するとグループ化した項目の昇順。")] AggregateOrder[]? sort = null,
                 [Description("返すグループ数の上限 (上限あり)。")] int? limit = null)
                    => AggregateRecordsAsync(moduleName, purpose, measures, filters, matchAny, groupBy, having, sort, limit, context),
                "aggregate_records",
                "条件に合うレコードを今のユーザーの権限で DB 側で集計する (件数・値の種類数・合計・平均・最小・最大。項目でグループ化、日付は単位でまとめ、集計後の絞り込み・並び・上限を指定できる)。結果は groups (key と集計値) と totalCount / groupCount / limited。");
            yield return AIFunctionFactory.Create(
                ([Description("モジュール名 (list_modules の名前)。")] string moduleName,
                 [Description("このツール呼び出しで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose,
                 [Description("行にする項目 (1 つ以上)。")] GroupBy[] rows,
                 [Description("集計値 (1 つ以上)。")] Measure[] measures,
                 [Description("列にする項目。省略すると行だけの表。")] GroupBy[]? columns = null,
                 [Description("絞り込み条件 (すべて満たす行)。項目の比較か、グループ (all / any。入れ子可)。省略で全行。")] Filter[]? filters = null,
                 [Description("true なら filters のどれかを満たす行 (OR)。")] bool matchAny = false,
                 [Description("集計後の絞り込み (行の小計に掛かる)。")] Having[]? having = null,
                 [Description("行の並び (group の index は rows の番号。measure なら行の小計で並ぶ)。省略すると行の項目の昇順。")] AggregateOrder[]? sort = null,
                 [Description("表に出す行の数の上限 (上限あり)。")] int? limit = null,
                 [Description("行ごとの合計・列ごとの合計・総計を付けるか (既定 true)。")] bool withTotals = true)
                    => CrossTabAsync(moduleName, purpose, rows, columns, measures, filters, matchAny, having, sort, limit, withTotals, context),
                "cross_tab",
                "条件に合うレコードを今のユーザーの権限で行 × 列のクロス表に集計する (DB 側で集計。合計も別に集計した正しい値)。結果は rows / columns (見出し)、cells[集計値名][行][列]、rowTotals / columnTotals / grandTotals、totalCount / groupCount / limited。");
        }

        async Task<string> FindRecordsAsync(string moduleName, string purpose, Filter[]? filters, bool matchAny, Sort[]? sort, string[]? fields, int? limit, int page, AIChatToolContext context)
        {
            var design = _design();
            var module = design?.Modules.Find((moduleName ?? string.Empty).Trim());
            if (design == null || module == null) return Error($"モジュール '{moduleName}' はありません。list_modules で名前を確かめてください。");

            SearchCondition condition;
            try
            {
                condition = BuildCondition(design, module, filters, matchAny);
                foreach (var s in sort ?? Array.Empty<Sort>())
                {
                    if (string.IsNullOrWhiteSpace(s.Field)) continue;
                    condition.SortConditions.Add(new SortCondition { Variable = ModuleDataConverter.ToVariable(s.Field), IsDescending = s.Descending });
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Error(e.Message);
            }
            try
            {
                condition.SelectFields = SelectFields(design, module, (fields ?? Array.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)));
            }
            catch (InvalidOperationException e)
            {
                return Error(e.Message);
            }
            condition.LimitCount = Math.Clamp(limit ?? _options.MaxRows, 1, _options.MaxRows);
            page = Math.Max(0, page);

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_ReadingRecords : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess find_records by {User} (conversation {Conversation}): module={Module} filters={Filters} page={Page}",
                context.Request.UserName, context.Request.ConversationId, module.Name, filters?.Length ?? 0, page);

            return await ReadAsync(module, "find_records", context, async scope =>
            {
                var result = await scope.ModuleDataIO.GetListAsync(condition, page);
                var rows = result.Items.Select(e => ModuleDataConverter.ToRow(design, module, e)).ToList();
                return Serialize(new Dictionary<string, object?>
                {
                    ["module"] = module.Name,
                    ["totalCount"] = result.TotalCount,
                    ["pageCount"] = result.PageCount,
                    ["page"] = page,
                    ["rowCount"] = rows.Count,
                    ["truncated"] = false,
                    ["rows"] = rows,
                }, rows);
            });
        }

        async Task<string> SearchRecordsAsync(string moduleName, string purpose, string query, int top, Filter[]? filters, bool matchAny, string[]? fields, AIChatToolContext context)
        {
            var design = _design();
            var provider = Provider();
            var semantic = design == null || provider == null ? new() : SemanticModules(design);
            var target = semantic.FirstOrDefault(e => string.Equals(e.Module.Name, (moduleName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (design == null || provider == null || target.Module == null)
                return Error($"モジュール '{moduleName}' は意味検索できません。使えるのは: {string.Join(", ", semantic.Select(e => e.Module.Name))}");
            if (string.IsNullOrWhiteSpace(query)) return Error("query が空です。");
            var module = target.Module;

            SearchCondition condition;
            try
            {
                condition = BuildCondition(design, module, filters, matchAny);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Error(e.Message);
            }
            try
            {
                condition.SelectFields = SelectFields(design, module, (fields ?? Array.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)));
            }
            catch (InvalidOperationException e)
            {
                return Error(e.Message);
            }
            condition.LimitCount = Math.Clamp(top, 1, _options.MaxRows);
            //近い順 (距離の昇順)。並べる式は SemanticSearchField が作る
            condition.SortConditions.Add(new SortCondition { Variable = ModuleDataConverter.ToVariable(target.Field.Name) });

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_SearchingRecords : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess search_records by {User} (conversation {Conversation}): module={Module} query={Query} filters={Filters}",
                context.Request.UserName, context.Request.ConversationId, module.Name, query, filters?.Length ?? 0);

            float[] vector;
            try
            {
                vector = (await provider.EmbedAsync(new[] { query }, context.CancellationToken))[0];
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess search_records embedding failed: {Message}", e.Message);
                return Error(e.Message);
            }
            var semanticCondition = new SemanticMatchCondition { FieldName = target.Field.Name, Vector = vector };
            condition.Condition = condition.Condition == null ? semanticCondition : MultiMatchCondition.And(condition.Condition, semanticCondition);

            return await ReadAsync(module, "search_records", context, async scope =>
            {
                var result = await scope.ModuleDataIO.GetListAsync(condition, 0);
                var rows = result.Items.Select(e => ModuleDataConverter.ToRow(design, module, e)).ToList();
                return Serialize(new Dictionary<string, object?>
                {
                    ["module"] = module.Name,
                    ["rowCount"] = rows.Count,
                    ["truncated"] = false,
                    ["rows"] = rows,
                }, rows);
            });
        }

        async Task<string> GetRecordAsync(string moduleName, string purpose, string id, AIChatToolContext context)
        {
            var design = _design();
            var module = design?.Modules.Find((moduleName ?? string.Empty).Trim());
            if (design == null || module == null) return Error($"モジュール '{moduleName}' はありません。list_modules で名前を確かめてください。");
            if (string.IsNullOrWhiteSpace(id)) return Error("id を指定してください。");

            var condition = new SearchCondition(module.Name)
            {
                Condition = new FieldValueMatchCondition { SearchTargetVariable = ModuleDataConverter.ToVariable(SystemFieldNames.Id), Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(id.Trim()) },
                LimitCount = 1,
            };
            //値の項目 + 子一覧 (ListField / ModuleField)。孫 (子一覧の中の子一覧) は読まない
            condition.SelectFields = SelectFields(design, module, Enumerable.Empty<string>())
                .Concat(module.Fields.Where(f => f is ListFieldDesignBase or ModuleFieldDesign).Select(f => f.Name)).Distinct().ToList();

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_ReadingRecords : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess get_record by {User} (conversation {Conversation}): module={Module} id={Id}", context.Request.UserName, context.Request.ConversationId, module.Name, id);

            return await ReadAsync(module, "get_record", context, async scope =>
            {
                var result = await scope.ModuleDataIO.GetListAsync(condition, 0);
                var data = result.Items.FirstOrDefault();
                if (data == null) return Error($"モジュール '{module.Name}' に Id '{id}' のレコードは無いか、今のユーザーには読めません。");

                var children = new Dictionary<string, object?>();
                var childLists = new List<Dictionary<string, object?>>();
                //件数上限 (ページング) のある一覧は本体が親と一緒に読まない (画面もページで読む)。無いものとして返すと AI が「明細は無い」と誤るので、読み方を添える
                foreach (var paged in module.Fields.OfType<ListFieldDesignBase>().Where(f => f.SearchCondition.LimitCount != null))
                {
                    children[paged.Name] = new Dictionary<string, object?>
                    {
                        ["module"] = paged.SearchCondition.ModuleName,
                        ["rowCount"] = null,
                        ["note"] = $"件数が多い一覧なのでここには含めていません。find_records でモジュール '{paged.SearchCondition.ModuleName}' をこのレコードの Id で絞って読んでください。",
                    };
                }
                foreach (var (name, field) in data.Fields)
                {
                    switch (field)
                    {
                        case ListFieldData list:
                        {
                            var childModule = design.Modules.Find((module.Fields.FirstOrDefault(f => f.Name == name) as ListFieldDesignBase)?.SearchCondition.ModuleName ?? string.Empty);
                            var entry = new Dictionary<string, object?>
                            {
                                ["module"] = childModule?.Name,
                                ["rowCount"] = list.Children.Count,
                                ["truncated"] = list.Children.Count > _options.MaxRows,
                                ["rows"] = list.Children.Take(_options.MaxRows).Select(c => ModuleDataConverter.ToRow(design, childModule, c)).ToList(),
                            };
                            children[name] = entry;
                            childLists.Add(entry);
                            break;
                        }
                        case ModuleFieldData child when !string.IsNullOrEmpty(child.Id):
                        {
                            var childModule = design.Modules.Find((module.Fields.FirstOrDefault(f => f.Name == name) as ModuleFieldDesign)?.ModuleName ?? string.Empty);
                            children[name] = new Dictionary<string, object?>
                            {
                                ["module"] = childModule?.Name,
                                ["id"] = child.Id,
                                ["row"] = ModuleDataConverter.ToRow(design, childModule, child.Data),
                            };
                            break;
                        }
                    }
                }
                var payload = new Dictionary<string, object?>
                {
                    ["module"] = module.Name,
                    ["id"] = id.Trim(),
                    ["row"] = ModuleDataConverter.ToRow(design, module, data),
                    ["children"] = children,
                };
                //結果が大きすぎれば、いちばん行の多い子一覧から半分に減らす (他のツールと同じ歯止め)
                while (true)
                {
                    var json = JsonSerializer.Serialize(payload);
                    if (json.Length <= _options.MaxResultChars) return json;
                    var largest = childLists.OrderByDescending(e => ((List<Dictionary<string, object?>>)e["rows"]!).Count).FirstOrDefault();
                    if (largest == null) return json;
                    var rows = (List<Dictionary<string, object?>>)largest["rows"]!;
                    if (rows.Count == 0) return json;
                    rows.RemoveRange(rows.Count / 2, rows.Count - rows.Count / 2);
                    largest["truncated"] = true;
                }
            });
        }

        //レコードを読むツールの共通の外枠: 1 返事の時間予算 → 同時実行の上限 → スコープを開いて (タイムアウトを入れて) 本体を呼ぶ → 権限拒否・時間切れを AI 向けのエラーに
        async Task<string> ReadAsync(ModuleDesign module, string tool, AIChatToolContext context, Func<ModuleDataAccessScope, Task<string>> body)
        {
            var budget = QueryTimeBudget.Of(context.Items, _options.MaxQuerySecondsPerReply);
            if (budget.IsExhausted)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess query time budget exhausted for {User} (conversation {Conversation})", context.Request.UserName, context.Request.ConversationId);
                return Error($"この返事でレコードの読み取りに使える時間 ({budget.LimitSeconds} 秒) を使い切りました。これ以上読まずに、ここまでの結果で答えてください。");
            }
            using var slot = await QueryGate.EnterAsync(module.DataSourceName ?? string.Empty, _options.MaxConcurrentQueries, _options.CommandTimeoutSeconds, context.CancellationToken);
            if (slot == null)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess gate wait timed out on {DataSource}", module.DataSourceName);
                return Error("データベースが混み合っています。少し待ってからもう一度試してください。");
            }

            var timeout = budget.CommandTimeoutSeconds(_options.CommandTimeoutSeconds);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await using var scope = await _openScope(context.Request.UserName);
                if (scope.DbAccessor != null && timeout > 0) scope.DbAccessor.CommandTimeoutSeconds = timeout;
                await scope.ModuleDataIO.CheckAppAuthorization();
                var result = await body(scope);
                context.Logger?.LogInformation("AIChat ModuleDataAccess {Tool} done by {User} on {Module}: {ElapsedMs} ms", tool, context.Request.UserName, module.Name, stopwatch.ElapsedMilliseconds);
                return result;
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
            catch (LowCodeAccessDeniedException e)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess denied for {User}: {Module} {Message}", context.Request.UserName, module.Name, e.Message);
                return Error(e.Message, accessDenied: true);
            }
            catch (Exception e)
            {
                var timedOut = timeout > 0 && stopwatch.Elapsed >= TimeSpan.FromSeconds(timeout) - TimeSpan.FromMilliseconds(500);
                context.Logger?.LogWarning("AIChat ModuleDataAccess {Tool} failed on {Module}: {ElapsedMs} ms, timedOut={TimedOut}: {Message}", tool, module.Name, stopwatch.ElapsedMilliseconds, timedOut, e.Message);
                return Error(timedOut
                    ? $"{timeout} 秒で時間切れになりました。同じ指定を繰り返さず、条件で絞るか、日付の単位を大きくするか、limit を減らしてください。(元のエラー: {e.Message})"
                    : e.Message);
            }
            finally
            {
                budget.Add(stopwatch.Elapsed);
            }
        }

        async Task<string> AggregateRecordsAsync(string moduleName, string purpose, Measure[] measures, Filter[]? filters, bool matchAny,
            GroupBy[]? groupBy, Having[]? having, AggregateOrder[]? sort, int? limit, AIChatToolContext context)
        {
            var design = _design();
            var module = design?.Modules.Find((moduleName ?? string.Empty).Trim());
            if (design == null || module == null) return Error($"モジュール '{moduleName}' はありません。list_modules で名前を確かめてください。");

            AggregateCondition condition;
            try
            {
                condition = BuildAggregateCondition(design, module, filters, matchAny, groupBy, measures, having, sort, limit, _options.MaxGroups);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Error(e.Message);
            }

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_Aggregating : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess aggregate_records by {User} (conversation {Conversation}): module={Module} filters={Filters} groupBy={GroupBy} measures={Measures}",
                context.Request.UserName, context.Request.ConversationId, module.Name, filters?.Length ?? 0,
                string.Join(",", condition.Groups.Select(g => g.Variable)), string.Join(",", condition.Measures.Select(MeasureName)));

            return await ReadAsync(module, "aggregate_records", context, async scope =>
            {
                var result = await scope.ModuleDataIO.AggregateAsync(condition);

                var groups = result.Rows.Select(row =>
                {
                    var json = new Dictionary<string, object?>();
                    if (condition.Groups.Count > 0)
                        json["key"] = condition.Groups.Select((g, i) => (Name: ModuleDataConverter.ToFieldName(g.Variable), Value: ModuleDataConverter.KeyToJson(row.Keys[i], g, design, module)))
                            .ToDictionary(e => e.Name, e => e.Value);
                    for (var i = 0; i < condition.Measures.Count; i++) json[MeasureName(condition.Measures[i])] = ModuleDataConverter.ToJsonValue(row.Value(i));
                    return json;
                }).ToList();
                return Serialize(new Dictionary<string, object?>
                {
                    ["module"] = module.Name,
                    ["totalCount"] = result.TotalCount,
                    ["groupCount"] = result.GroupCount,
                    ["limited"] = result.IsLimited,
                    ["truncated"] = false,
                    ["groups"] = groups,
                }, groups);
            });
        }

        async Task<string> CrossTabAsync(string moduleName, string purpose, GroupBy[] rows, GroupBy[]? columns, Measure[] measures, Filter[]? filters, bool matchAny,
            Having[]? having, AggregateOrder[]? sort, int? limit, bool withTotals, AIChatToolContext context)
        {
            var design = _design();
            var module = design?.Modules.Find((moduleName ?? string.Empty).Trim());
            if (design == null || module == null) return Error($"モジュール '{moduleName}' はありません。list_modules で名前を確かめてください。");
            var rowGroups = (rows ?? Array.Empty<GroupBy>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Field)).ToArray();
            var columnGroups = (columns ?? Array.Empty<GroupBy>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Field)).ToArray();
            if (rowGroups.Length == 0) return Error("rows (行にする項目) を 1 つ以上指定してください。");

            AggregateCondition condition;
            try
            {
                //sort の group の番号は rows の番号 (列の並びは CrossTabBuilder が列の項目の順にする)。指定が無ければ行の項目の昇順
                condition = BuildAggregateCondition(design, module, filters, matchAny, rowGroups.Concat(columnGroups).ToArray(), measures, having, sort, limit, _options.MaxGroups);
                if (sort?.Any(s => s != null) == true)
                {
                    if (condition.SortConditions.Any(s => s.Target == AggregateSortTarget.Group && s.Index >= rowGroups.Length))
                        throw new InvalidOperationException("sort の group の index は rows の番号です (列は項目の順に並びます)。");
                }
                else
                {
                    condition.SortConditions.RemoveAll(s => s.Index >= rowGroups.Length);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Error(e.Message);
            }

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_Aggregating : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess cross_tab by {User} (conversation {Conversation}): module={Module} filters={Filters} rows={Rows} columns={Columns} measures={Measures}",
                context.Request.UserName, context.Request.ConversationId, module.Name, filters?.Length ?? 0,
                string.Join(",", condition.Groups.Take(rowGroups.Length).Select(g => g.Variable)), string.Join(",", condition.Groups.Skip(rowGroups.Length).Select(g => g.Variable)),
                string.Join(",", condition.Measures.Select(MeasureName)));

            return await ReadAsync(module, "cross_tab", context, async scope =>
            {
                var table = await CrossTabBuilder.BuildAsync(condition, rowGroups.Length, scope.ModuleDataIO.AggregateAsync, withTotals, _options.MaxCrossTabCells);
                if (table == null) return Error("集計に失敗しました。");

                var rowGroupList = condition.Groups.Take(rowGroups.Length).ToList();
                var columnGroupList = condition.Groups.Skip(rowGroups.Length).ToList();
                var measureNames = condition.Measures.Select(MeasureName).ToList();
                var payload = new Dictionary<string, object?>
                {
                    ["module"] = module.Name,
                    ["rows"] = table.Rows.Select(r => AxisToJson(r, rowGroupList, design, module)).ToList(),
                    ["columns"] = columnGroupList.Count == 0 ? new List<object?>() : table.Columns.Select(c => AxisToJson(c, columnGroupList, design, module)).ToList(),
                    ["measures"] = measureNames,
                    //列の軸が無い表は列が 1 つ (鍵なし) なので、セルは [行] だけにする
                    ["cells"] = measureNames.Select((name, m) => (name, cells: columnGroupList.Count == 0
                            ? table.Cells[m].Select(r => ModuleDataConverter.ToJsonValue(r[0].GetValue())).ToList()
                            : (object)table.Cells[m].Select(r => r.Select(c => ModuleDataConverter.ToJsonValue(c.GetValue())).ToList()).ToList()))
                        .ToDictionary(e => e.name, e => (object?)e.cells),
                    ["totalCount"] = table.TotalCount,
                    ["groupCount"] = table.GroupCount,
                    ["limited"] = table.IsLimited,
                };
                if (table.HasTotals)
                {
                    if (columnGroupList.Count > 0)
                    {
                        payload["rowTotals"] = measureNames.Select((name, m) => (name, totals: table.RowTotals[m].Select(v => ModuleDataConverter.ToJsonValue(v.GetValue())).ToList())).ToDictionary(e => e.name, e => (object?)e.totals);
                        payload["columnTotals"] = measureNames.Select((name, m) => (name, totals: table.ColumnTotals[m].Select(v => ModuleDataConverter.ToJsonValue(v.GetValue())).ToList())).ToDictionary(e => e.name, e => (object?)e.totals);
                    }
                    payload["grandTotals"] = measureNames.Select((name, m) => (name, total: ModuleDataConverter.ToJsonValue(table.GrandTotals[m].GetValue()))).ToDictionary(e => e.name, e => e.total);
                }
                var json = JsonSerializer.Serialize(payload);
                if (json.Length > _options.MaxResultChars)
                    return Error($"表が大きすぎます ({table.Rows.Count} 行 × {table.Columns.Count} 列)。条件で絞るか、日付の単位を大きくするか、limit で行を減らしてください。");
                return json;
            });
        }

        //軸の値 1 つ (複数の軸を重ねたときは鍵が複数) を { key: { 項目: 値 }, text } に
        static Dictionary<string, object?> AxisToJson(CrossTabAxisValue axis, List<AggregateGroup> groups, DesignData design, ModuleDesign module)
        {
            var key = groups.Select((g, i) => (Name: ModuleDataConverter.ToFieldName(g.Variable), Value: ModuleDataConverter.KeyToJson(axis.Keys[i], g, design, module))).ToDictionary(e => e.Name, e => e.Value);
            var text = string.Join(" / ", key.Values.Select(v => v is Dictionary<string, object?> d && d.TryGetValue("text", out var t) ? Convert.ToString(t, CultureInfo.InvariantCulture) : v == null ? string.Empty : Convert.ToString(v, CultureInfo.InvariantCulture)));
            return new Dictionary<string, object?> { ["key"] = key, ["text"] = text };
        }

        //集計値の名前 (結果の JSON のキー): count / countDistinct_Field / sum_Field ...
        static string MeasureName(AggregateMeasure measure)
        {
            var function = char.ToLowerInvariant(measure.Function.ToString()[0]) + measure.Function.ToString()[1..];
            return measure.Function == AggregateFunction.Count ? "count" : function + "_" + ModuleDataConverter.ToFieldName(measure.Variable);
        }

        //AI の集計指定を本体の集計定義に。項目が設計に無い・その項目に使えない集計や単位・番号の範囲外は例外 (メッセージを AI に返す)
        AggregateCondition BuildAggregateCondition(DesignData design, ModuleDesign module, Filter[]? filters, bool matchAny,
            GroupBy[]? groupBy, Measure[]? measures, Having[]? having, AggregateOrder[]? sort, int? limit, int maxGroups)
        {
            var search = BuildCondition(design, module, filters, matchAny);
            var condition = new AggregateCondition(module.Name) { Condition = search.Condition };

            foreach (var g in (groupBy ?? Array.Empty<GroupBy>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Field)))
            {
                var field = FindFieldOrThrow(design, module, g.Field);
                AggregateGroup group;
                if (!string.IsNullOrWhiteSpace(g.DateUnit))
                {
                    var bucket = ModuleDataConverter.ParseBucket(g.DateUnit)
                        ?? throw new InvalidOperationException($"日付の単位 '{g.DateUnit}' は使えません (year / quarter / month / week / day / hour)。");
                    var fiscal = g.FiscalYearStartMonth ?? _options.FiscalYearStartMonth;
                    if (fiscal is < 1 or > 12) throw new InvalidOperationException($"年度の開始月 '{fiscal}' は 1〜12 で指定してください。");
                    group = new DateGroup { Bucket = bucket, FiscalYearStartMonth = fiscal };
                }
                else
                {
                    group = new ValueGroup();
                }
                group.Variable = ModuleDataConverter.ToVariable(g.Field);
                if (!group.CanApplyTo(field))
                    throw new InvalidOperationException(group is DateGroup
                        ? $"項目 '{g.Field}' は日付・日時ではないので dateUnit は使えません。"
                        : $"項目 '{g.Field}' ではグループ化できません。");
                condition.Groups.Add(group);
            }

            var measureList = (measures ?? Array.Empty<Measure>()).Where(m => m != null).ToList();
            if (measureList.Count == 0) throw new InvalidOperationException("measures を 1 つ以上指定してください (例: count、sum Amount)。");
            foreach (var m in measureList)
            {
                var function = ModuleDataConverter.ParseFunction(m.Function)
                    ?? throw new InvalidOperationException($"集計関数 '{m.Function}' は使えません (count / countDistinct / sum / avg / min / max)。");
                var measure = new AggregateMeasure { Function = function };
                if (function != AggregateFunction.Count)
                {
                    if (string.IsNullOrWhiteSpace(m.Field)) throw new InvalidOperationException($"{m.Function} には field が要ります。");
                    var field = FindFieldOrThrow(design, module, m.Field);
                    if (!measure.CanApplyTo(field)) throw new InvalidOperationException($"項目 '{m.Field}' に {m.Function} は使えません (sum / avg は数値項目、min / max は数値・日付・日時・時刻・文字)。");
                    measure.Variable = ModuleDataConverter.ToVariable(m.Field);
                }
                condition.Measures.Add(measure);
            }

            foreach (var h in (having ?? Array.Empty<Having>()).Where(h => h != null))
            {
                if (h.MeasureIndex < 0 || h.MeasureIndex >= condition.Measures.Count) throw new InvalidOperationException($"having の measureIndex '{h.MeasureIndex}' は measures の範囲外です (0〜{condition.Measures.Count - 1})。");
                var comparison = ModuleDataConverter.ParseComparison(h.Comparison);
                if (comparison is null or MatchComparison.Like or MatchComparison.In or MatchComparison.NotIn)
                    throw new InvalidOperationException($"having の比較 '{h.Comparison}' は使えません (Equal / NotEqual / LessThan / LessThanOrEqual / GreaterThan / GreaterThanOrEqual)。");
                condition.Having.Add(new AggregateHaving { MeasureIndex = h.MeasureIndex, Comparison = comparison.Value, Value = h.Value });
            }

            foreach (var s in (sort ?? Array.Empty<AggregateOrder>()).Where(s => s != null))
            {
                var target = (s.Target ?? string.Empty).Trim().ToLowerInvariant() switch
                {
                    "" or "group" or "key" => AggregateSortTarget.Group,
                    "measure" or "value" => AggregateSortTarget.Measure,
                    _ => throw new InvalidOperationException($"sort の target '{s.Target}' は使えません (group / measure)。"),
                };
                var count = target == AggregateSortTarget.Group ? condition.Groups.Count : condition.Measures.Count;
                if (s.Index < 0 || s.Index >= count) throw new InvalidOperationException($"sort の index '{s.Index}' は {(target == AggregateSortTarget.Group ? "groupBy" : "measures")} の範囲外です (0〜{count - 1})。");
                condition.SortConditions.Add(new AggregateSort { Target = target, Index = s.Index, IsDescending = s.Descending });
            }
            //並びの指定が無ければグループ化した項目の昇順 (DB 任せにすると順が不定)
            if (condition.SortConditions.Count == 0)
                for (var i = 0; i < condition.Groups.Count; i++) condition.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Group, Index = i });

            condition.LimitCount = Math.Clamp(limit ?? maxGroups, 1, maxGroups);
            return condition;
        }

        static FieldDesignBase FindFieldOrThrow(DesignData design, ModuleDesign module, string field)
            => ModuleDataConverter.FindField(design, module, field)
                ?? throw new InvalidOperationException($"項目 '{field}' はモジュール '{module.Name}' にありません。describe_module で項目名を確かめてください。");

        //読む項目。指定が無ければ DB 列を持つ全項目 (子一覧は除く)。Id は常に。指定に無い項目があれば例外 (メッセージを AI に返す)。
        //選択・リンクの表示名は、モジュールにあるリンク越しフィールド ("Customer.Name") を一緒に読んだときだけ分かるので足す
        static List<string> SelectFields(DesignData design, ModuleDesign module, IEnumerable<string> requested)
        {
            var names = requested.Select(f => ModuleDataConverter.ToFieldName(f)).Where(f => f.Length > 0).Distinct().ToList();
            //無い項目 (表示名で頼んだ等) を黙って落とすと、AI は値が無いと受け取る。条件と同じく項目名を確かめるよう返す
            foreach (var name in names) FindFieldOrThrow(design, module, name);
            if (names.Count == 0) names = module.Fields.Where(f => f is DbValueFieldDesignBase).Select(f => f.Name).ToList();
            if (!names.Contains(SystemFieldNames.Id)) names.Insert(0, SystemFieldNames.Id);
            foreach (var name in names.ToList())
            {
                var path = ModuleDataConverter.DisplayTextPath(design, module, name);
                if (path != null && !names.Contains(path)) names.Add(path);
            }
            return names;
        }

        //AI の条件を本体の検索条件に。項目が設計に無ければ例外 (メッセージを AI に返す)
        static SearchCondition BuildCondition(DesignData design, ModuleDesign module, Filter[]? filters, bool matchAny)
        {
            var condition = new SearchCondition(module.Name);
            var children = ToConditions(design, module, filters);
            if (children.Count > 0) condition.Condition = new MultiMatchCondition { IsOrMatch = matchAny, Children = children };
            return condition;
        }

        static List<MatchConditionBase> ToConditions(DesignData design, ModuleDesign module, Filter[]? filters)
            => (filters ?? Array.Empty<Filter>()).Where(f => f != null).Select(f => ToCondition(design, module, f)).Where(c => c != null).Select(c => c!).ToList();

        //1 つの条件を本体の条件に。グループ (All / Any) は MultiMatchCondition の入れ子、Not は IsNot (項目の比較の否定は 1 要素のグループで包む)
        static MatchConditionBase? ToCondition(DesignData design, ModuleDesign module, Filter filter)
        {
            var hasAll = filter.All is { Length: > 0 };
            var hasAny = filter.Any is { Length: > 0 };
            if (hasAll && hasAny) throw new InvalidOperationException("1 つの条件に all と any を同時には指定できません。どちらかを入れ子にしてください。");
            if (hasAll || hasAny)
            {
                var children = ToConditions(design, module, hasAll ? filter.All : filter.Any);
                if (children.Count == 0) return null;
                return new MultiMatchCondition { IsOrMatch = hasAny, IsNot = filter.Not, Children = children };
            }
            if (string.IsNullOrWhiteSpace(filter.Field)) return null;

            var field = ModuleDataConverter.FindField(design, module, filter.Field)
                ?? throw new InvalidOperationException($"項目 '{filter.Field}' はモジュール '{module.Name}' にありません。describe_module で項目名を確かめてください。");
            var comparison = ModuleDataConverter.ParseComparison(filter.Comparison)
                ?? throw new InvalidOperationException($"比較 '{filter.Comparison}' は使えません (Equal / NotEqual / LessThan / LessThanOrEqual / GreaterThan / GreaterThanOrEqual / Like / In / NotIn)。");
            var leaf = new FieldValueMatchCondition
            {
                SearchTargetVariable = ModuleDataConverter.ToVariable(filter.Field),
                Comparison = comparison,
                Value = ModuleDataConverter.ToMatchValue(filter.Value, field),
            };
            return filter.Not ? new MultiMatchCondition { IsNot = true, Children = { leaf } } : leaf;
        }

        static string Error(string message, bool accessDenied = false)
            => JsonSerializer.Serialize(accessDenied ? new { error = message, accessDenied = true } : (object)new { error = message });

        //結果が大きすぎれば行を半分に減らす (RawDataAccess と同じ歯止め)
        string Serialize(Dictionary<string, object?> payload, List<Dictionary<string, object?>> rows)
        {
            while (true)
            {
                var json = JsonSerializer.Serialize(payload);
                if (json.Length <= _options.MaxResultChars || rows.Count == 0) return json;
                rows.RemoveRange(rows.Count / 2, rows.Count - rows.Count / 2);
                payload["truncated"] = true;
                if (payload.ContainsKey("rowCount")) payload["rowCount"] = rows.Count;
            }
        }
    }
}
