using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// AI にアプリのレコードを「実行ユーザーの権限で」読ませるツール群: <c>find_records</c> (条件・並び・件数を指定して行を返す) と
    /// <c>aggregate_records</c> (条件に合う行をサーバーで数える・足す)。どちらも本体の <see cref="ModuleDataIO.GetListAsync"/> を通るので、
    /// モジュールの閲覧権限 (UserRead)・行の条件 (DataRead)・項目の読み取り権限 (PermissionField)・論理削除・アプリアクセス条件が画面と同じに効く。
    /// SQL は書かせない (生 SQL で DB を読むのは <see cref="RawDataAccess.RawDataAccessToolSet"/>)。
    /// <para>
    /// 行を読む ModuleDataIO はツール呼び出しごとに <see cref="ModuleDataAccessScope"/> として開いて閉じる (バックグラウンド実行のためリクエストの寿命に乗れない)。
    /// 誰の権限で開くかは依頼のユーザー (<see cref="AIChatAgentRequest.UserName"/> = AIChatService.StartAsync の ownerKey) で決まる。
    /// </para>
    /// </summary>
    internal sealed class ModuleDataAccessToolSet : IAIChatToolSet
    {
        const int AggregatePageSize = 500;

        readonly Func<string, Task<ModuleDataAccessScope>> _openScope;
        readonly Func<DesignData?> _design;
        readonly ModuleDataAccessOptions _options;

        /// <param name="openScope">ユーザー Id → そのユーザーの権限を持つ ModuleDataIO (ツール呼び出しごとに開いて閉じる)</param>
        /// <param name="design">デザイン定義 (ホットリロードで変わるので都度取る)。項目の型と候補値の解決に使う</param>
        public ModuleDataAccessToolSet(Func<string, Task<ModuleDataAccessScope>> openScope, Func<DesignData?> design, ModuleDataAccessOptions options)
        {
            _openScope = openScope;
            _design = design;
            _options = options;
        }

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

        /// <summary>aggregate_records のグループ化の項目。</summary>
        internal sealed class GroupBy
        {
            [Description("項目名。")]
            public string Field { get; set; } = string.Empty;
            [Description("日付・日時の項目を丸める単位: year / month / day。それ以外の項目では省略。")]
            public string? DateUnit { get; set; }
        }

        /// <summary>aggregate_records の集計値。</summary>
        internal sealed class Measure
        {
            [Description("count / sum / avg / min / max。")]
            public string Function { get; set; } = "count";
            [Description("集計する数値項目の名前。count では省略。")]
            public string? Field { get; set; }
        }

        public string GetInstructions(AIChatToolContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("アプリのレコードを読むことができます (find_records / aggregate_records)。読めるのは、今のユーザーがアプリの画面で見られるレコードと項目だけです (モジュールの閲覧権限・行の条件・項目の読み取り権限が効きます。削除済みの行は出ません)。");
            sb.AppendLine("- 権限が無いモジュールを読むとエラー (error と accessDenied=true) になります。そのときは読めなかったことをユーザーに伝えてください。別の経路で読もうとしないこと。");
            sb.AppendLine("- 絞り込みは filters に項目の比較 ({ field, comparison, value }) を並べます (すべて満たす = AND。matchAny で OR)。AND と OR を組み合わせるときは、条件の代わりにグループ ({ all: [...] } / { any: [...] }) を置きます (入れ子可)。not: true で否定できます。");
            sb.AppendLine("- 手順: まず補足文書と describe_module で項目名・候補値・リンクを確かめ、それから find_records / aggregate_records を呼びます。項目は describe_module に出るフィールド名で指定します (DB の列名ではありません)。リンク先の項目は、describe_module にリンク越しの項目 (例: Customer.Name) として出ているものだけ使えます。SQL は書けません。");
            sb.AppendLine("- 候補値のある項目 (状態など) は、条件にコード (候補値の左側) を使います。結果では { value, text } の形で値と表示名の両方が返ります。");
            sb.AppendLine("- 件数・合計・平均などは aggregate_records で求めてください (サーバーで計算します)。行を取って自分で足し算しないこと。");
            sb.AppendLine($"- find_records は 1 回に最大 {_options.MaxRows} 行です。fields で必要な項目だけに絞ると結果が小さくなります。続きは page を進めて取ります (totalCount と pageCount が返ります)。");
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
                 [Description("集計値 (1 つ以上)。")] Measure[] measures,
                 [Description("絞り込み条件 (すべて満たす行)。項目の比較か、グループ (all / any。入れ子可)。省略で全行。")] Filter[]? filters = null,
                 [Description("true なら filters のどれかを満たす行 (OR)。")] bool matchAny = false,
                 [Description("グループ化する項目 (省略で全体を 1 グループ)。")] GroupBy[]? groupBy = null)
                    => AggregateRecordsAsync(moduleName, purpose, measures, filters, matchAny, groupBy, context),
                "aggregate_records",
                "条件に合うレコードを今のユーザーの権限で読み、サーバーで件数・合計・平均・最小・最大を求める (項目でグループ化できる)。結果は groups (key と集計値)。");
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
            condition.SelectFields = SelectFields(design, module, (fields ?? Array.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)));
            condition.LimitCount = Math.Clamp(limit ?? _options.MaxRows, 1, _options.MaxRows);
            page = Math.Max(0, page);

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_ReadingRecords : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess find_records by {User} (conversation {Conversation}): module={Module} filters={Filters} page={Page}",
                context.Request.UserName, context.Request.ConversationId, module.Name, filters?.Length ?? 0, page);

            try
            {
                await using var scope = await _openScope(context.Request.UserName);
                await scope.ModuleDataIO.CheckAppAuthorization();
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
            }
            catch (OperationCanceledException) { throw; }
            catch (LowCodeAccessDeniedException e)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess denied for {User}: {Module} {Message}", context.Request.UserName, module.Name, e.Message);
                return Error(e.Message, accessDenied: true);
            }
            catch (Exception e)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess find_records failed: {Message}", e.Message);
                return Error(e.Message);
            }
        }

        async Task<string> AggregateRecordsAsync(string moduleName, string purpose, Measure[] measures, Filter[]? filters, bool matchAny, GroupBy[]? groupBy, AIChatToolContext context)
        {
            var design = _design();
            var module = design?.Modules.Find((moduleName ?? string.Empty).Trim());
            if (design == null || module == null) return Error($"モジュール '{moduleName}' はありません。list_modules で名前を確かめてください。");
            var measureList = (measures ?? Array.Empty<Measure>()).Where(m => m != null).ToList();
            if (measureList.Count == 0) return Error("measures を 1 つ以上指定してください (例: count、sum Amount)。");
            foreach (var m in measureList)
            {
                var function = (m.Function ?? string.Empty).Trim().ToLowerInvariant();
                if (function is not ("count" or "sum" or "avg" or "min" or "max")) return Error($"集計関数 '{m.Function}' は使えません (count / sum / avg / min / max)。");
                if (function != "count" && string.IsNullOrWhiteSpace(m.Field)) return Error($"{function} には field (数値項目) が要ります。");
            }
            var groups = (groupBy ?? Array.Empty<GroupBy>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Field)).ToList();

            SearchCondition condition;
            try
            {
                condition = BuildCondition(design, module, filters, matchAny);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return Error(e.Message);
            }
            //読む項目は集計に要るものだけ
            condition.SelectFields = SelectFields(design, module, groups.Select(g => g.Field).Concat(measureList.Where(m => !string.IsNullOrWhiteSpace(m.Field)).Select(m => m.Field!)));
            condition.LimitCount = AggregatePageSize;

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_Aggregating : purpose.Trim());
            context.Logger?.LogInformation("AIChat ModuleDataAccess aggregate_records by {User} (conversation {Conversation}): module={Module} filters={Filters} groupBy={GroupBy} measures={Measures}",
                context.Request.UserName, context.Request.ConversationId, module.Name, filters?.Length ?? 0,
                string.Join(",", groups.Select(g => g.Field)), string.Join(",", measureList.Select(m => m.Function + ":" + m.Field)));

            try
            {
                await using var scope = await _openScope(context.Request.UserName);
                await scope.ModuleDataIO.CheckAppAuthorization();

                var accumulator = new Dictionary<string, Group>(StringComparer.Ordinal);
                var scanned = 0;
                var truncated = false;
                for (var page = 0; ; page++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    var result = await scope.ModuleDataIO.GetListAsync(condition, page);
                    foreach (var data in result.Items)
                    {
                        if (scanned >= _options.MaxAggregateRows) { truncated = true; break; }
                        scanned++;
                        Accumulate(accumulator, design, module, data, groups, measureList);
                    }
                    if (truncated || result.Items.Count < AggregatePageSize || page + 1 >= result.PageCount) break;
                }

                var first = measureList[0];
                var ordered = accumulator.Values
                    .OrderByDescending(g => g.MeasureValue(first) ?? decimal.MinValue)
                    .ThenBy(g => g.KeyText, StringComparer.Ordinal)
                    .ToList();
                var groupRows = ordered.Take(_options.MaxGroups).Select(g => g.ToJson(measureList)).ToList();
                return Serialize(new Dictionary<string, object?>
                {
                    ["module"] = module.Name,
                    ["scannedRows"] = scanned,
                    ["truncated"] = truncated,
                    ["groupCount"] = accumulator.Count,
                    ["groups"] = groupRows,
                }, groupRows);
            }
            catch (OperationCanceledException) { throw; }
            catch (LowCodeAccessDeniedException e)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess denied for {User}: {Module} {Message}", context.Request.UserName, module.Name, e.Message);
                return Error(e.Message, accessDenied: true);
            }
            catch (Exception e)
            {
                context.Logger?.LogWarning("AIChat ModuleDataAccess aggregate_records failed: {Message}", e.Message);
                return Error(e.Message);
            }
        }

        //読む項目。指定が無ければ DB 列を持つ全項目 (子一覧は除く)。Id は常に。
        //選択・リンクの表示名は、モジュールにあるリンク越しフィールド ("Customer.Name") を一緒に読んだときだけ分かるので足す
        static List<string> SelectFields(DesignData design, ModuleDesign module, IEnumerable<string> requested)
        {
            var names = requested.Select(f => ModuleDataConverter.ToFieldName(f)).Where(f => f.Length > 0).Distinct().ToList();
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

        static void Accumulate(Dictionary<string, Group> accumulator, DesignData design, ModuleDesign module, ModuleData data, List<GroupBy> groups, List<Measure> measures)
        {
            var row = ModuleDataConverter.ToRow(design, module, data);
            var keys = new List<KeyValuePair<string, string>>();
            foreach (var g in groups)
            {
                var name = ModuleDataConverter.ToFieldName(g.Field);
                data.Fields.TryGetValue(name, out var fieldData);
                row.TryGetValue(name, out var rowValue);
                keys.Add(new(name, ModuleDataConverter.GroupKey(rowValue, fieldData, g.DateUnit)));
            }
            var keyText = string.Join("\u001f", keys.Select(k => k.Value));
            if (!accumulator.TryGetValue(keyText, out var group))
            {
                group = new Group(keys, keyText);
                accumulator[keyText] = group;
            }
            group.Count++;
            foreach (var m in measures)
            {
                if (string.IsNullOrWhiteSpace(m.Field)) continue;
                var name = ModuleDataConverter.ToFieldName(m.Field);
                data.Fields.TryGetValue(name, out var fieldData);
                var number = ToDecimal(ModuleDataConverter.RawValue(fieldData));
                if (number != null) group.Add(name, number.Value);
            }
        }

        static decimal? ToDecimal(object? value) => value switch
        {
            null => null,
            decimal d => d,
            int i => i,
            long l => l,
            double d => (decimal)d,
            float f => (decimal)f,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

        sealed class Group
        {
            readonly Dictionary<string, (decimal Sum, decimal Min, decimal Max, int Count)> _numbers = new();

            public Group(List<KeyValuePair<string, string>> keys, string keyText)
            {
                Keys = keys;
                KeyText = keyText;
            }

            public List<KeyValuePair<string, string>> Keys { get; }
            public string KeyText { get; }
            public int Count { get; set; }

            public void Add(string field, decimal value)
            {
                if (_numbers.TryGetValue(field, out var n))
                    _numbers[field] = (n.Sum + value, Math.Min(n.Min, value), Math.Max(n.Max, value), n.Count + 1);
                else
                    _numbers[field] = (value, value, value, 1);
            }

            public decimal? MeasureValue(Measure measure)
            {
                var function = (measure.Function ?? string.Empty).Trim().ToLowerInvariant();
                if (function == "count") return Count;
                var field = ModuleDataConverter.ToFieldName(measure.Field ?? string.Empty);
                if (!_numbers.TryGetValue(field, out var n)) return null;
                return function switch
                {
                    "sum" => n.Sum,
                    "avg" => n.Count == 0 ? null : n.Sum / n.Count,
                    "min" => n.Min,
                    "max" => n.Max,
                    _ => null,
                };
            }

            public Dictionary<string, object?> ToJson(List<Measure> measures)
            {
                var json = new Dictionary<string, object?>();
                if (Keys.Count > 0) json["key"] = Keys.ToDictionary(k => k.Key, k => (object?)k.Value);
                foreach (var m in measures)
                {
                    var function = (m.Function ?? string.Empty).Trim().ToLowerInvariant();
                    var name = function == "count" ? "count" : function + "_" + ModuleDataConverter.ToFieldName(m.Field ?? string.Empty);
                    json[name] = MeasureValue(m);
                }
                return json;
            }
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
