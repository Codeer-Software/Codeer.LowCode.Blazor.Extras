using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.DesignLogic;
using CoreServices = Codeer.LowCode.Blazor.RequestInterfaces.Services;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    /// <summary>
    /// タグの候補と表記 (TagField / TagInputField 共通。フィールドごとに 1 つ)。
    /// 候補はサーバーで絞る: 列の値を「名前でグループ化・件数・件数の多い順・上位 N 件・打った文字で部分一致」で 1 回引く (本体の集計)。
    /// 行の読み取り条件 (DataRead) はそのまま効く。決まったタグ (Values) は問い合わせない。
    /// </summary>
    internal sealed class TagCandidateProvider
    {
        /// <summary>1 回に引く数 (付いているタグを除いてから画面は 10 件出す)。設定にはしない。</summary>
        internal const int QueryLimit = 20;

        readonly Func<CoreServices> _services;
        readonly Func<(string Module, string Field)?> _column;
        readonly List<string>? _values;
        //引いたことのある表記 (大文字小文字を区別しない名前 → 使われている表記)
        readonly Dictionary<string, string> _known = new(StringComparer.OrdinalIgnoreCase);

        TagCandidateProvider(Func<CoreServices> services, Func<(string, string)?> column, List<string>? values)
        {
            _services = services;
            _column = column;
            _values = values;
            foreach (var value in values ?? new()) _known[value] = value;
        }

        /// <summary>
        /// 設定から作る。own = TagField 自身のタグ付け (TagRows 用。TagInputField は null)。
        /// 列はフィールドが使われるときに解決する (デザインの読み込みより先に作られることがあるため)。
        /// </summary>
        internal static TagCandidateProvider Create(Func<CoreServices> services, ITagCandidateDesign design, Func<TagBinding?> own)
        {
            if (design.CandidateSource == TagCandidateSource.Values)
                return new TagCandidateProvider(services, () => null, TagCandidateChecks.ParseValues(design.CandidateValues));
            return new TagCandidateProvider(services, () => Column(services().AppInfoService.GetDesignData(), design, own), null);
        }

        //集計する列: TagRows = 自分のタグ付けのタグ名、Module = 指定の列 (TagField ならそのタグ付けのタグ名)
        static (string, string)? Column(DesignData design, ITagCandidateDesign settings, Func<TagBinding?> own)
        {
            if (settings.CandidateSource == TagCandidateSource.TagRows)
                return own() is { } binding ? (binding.LinkModule, binding.TagNameField) : null;
            var field = design.Modules.Find(settings.CandidateModuleName)?.Fields.FirstOrDefault(e => e.Name == settings.CandidateFieldName);
            return field switch
            {
                TagFieldDesign tagField => TagContracts.Resolve(design, tagField) is { } binding ? (binding.LinkModule, binding.TagNameField) : null,
                TextFieldDesign => (settings.CandidateModuleName, settings.CandidateFieldName),
                _ => null,
            };
        }

        /// <summary>打った文字を含む候補 (よく使われている順。何も打っていなければよく使われているタグ)。</summary>
        internal async Task<List<string>> SuggestAsync(string text)
        {
            text = text.Trim();
            if (_values != null)
                return text.Length == 0 ? _values.ToList() : _values.Where(e => e.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            var condition = text.Length == 0 ? null
                : new FieldValueMatchCondition { Comparison = MatchComparison.Like, Value = new StringValue { Value = text } };
            return await QueryAsync(condition, QueryLimit);
        }

        /// <summary>
        /// 既にある表記 (大文字小文字を区別しない。いちばん使われている表記)。無ければ null。
        /// 引いたことのある名前は問い合わせない。query = false なら引いたことのある名前だけで答える。
        /// </summary>
        internal async Task<string?> FindSpellingAsync(string name, bool query)
        {
            if (_known.TryGetValue(name, out var known)) return known;
            if (_values != null || !query) return null;
            //照合順序が大文字小文字を区別しない DB (SQL Server・MySQL・SQLite の NOCASE 列) では表記違いもまとめて返る
            var found = await QueryAsync(new FieldValueMatchCondition { Comparison = MatchComparison.Equal, Value = new StringValue { Value = name } }, 5);
            return found.FirstOrDefault(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
        }

        async Task<List<string>> QueryAsync(FieldValueMatchCondition? condition, int limit)
        {
            var services = _services();
            if (services.AppInfoService.IsDesignMode || _column() is not { } column) return new();
            var module = services.AppInfoService.GetDesignData().Modules.Find(column.Module);
            if (module == null || !module.HasUserReadPermission(services)) return new();

            var variable = $"{column.Field}.Value";
            var aggregate = new AggregateCondition(column.Module)
            {
                Groups = { new ValueGroup { Variable = variable } },
                Measures = { new AggregateMeasure { Function = AggregateFunction.Count, Name = "count" } },
                SortConditions =
                {
                    new AggregateSort { Target = AggregateSortTarget.Measure, Index = 0, IsDescending = true },
                    new AggregateSort { Target = AggregateSortTarget.Group, Index = 0 },
                },
                LimitCount = limit,
            };
            if (condition != null)
            {
                condition.SearchTargetVariable = variable;
                aggregate.Condition = MultiMatchCondition.And(condition);
            }
            try
            {
                var result = (await services.ModuleDataService.AggregateAsync(new List<AggregateCondition> { aggregate })).FirstOrDefault();
                var names = (result?.Rows ?? new())
                    .Select(e => e.KeyText(0)?.Trim() ?? string.Empty)
                    .Where(e => e.Length > 0)
                    .ToList();
                //件数の多い順に来るので、表記違いは最初 (いちばん使われている表記) を覚える
                foreach (var name in names) _known.TryAdd(name, name);
                return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception e)
            {
                await services.Logger.Error(e.Message);
                return new();
            }
        }
    }
}
