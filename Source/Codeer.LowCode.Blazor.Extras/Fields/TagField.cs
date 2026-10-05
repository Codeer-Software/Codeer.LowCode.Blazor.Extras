using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// タグを保持する値フィールド。Value はタグを ", " でつないだ文字列 (DB に入る形)。
    /// 読むときは「,」「、」「，」のどれでも区切り、前後の空白・空のタグ・重複を落とす。
    /// タグの同一判定は大文字小文字を区別しない (先に入っていた表記を残す)。
    /// </summary>
    public class TagField(TagFieldDesign design)
        : ValueFieldBase<TagFieldDesign, TagFieldData, string>(design), ISearchableField
    {
        /// <summary>保存するときのタグの区切り。</summary>
        internal const string Separator = ", ";

        //候補を読む行数 (新しい行から。プロパティにはしない)
        const int CandidateRowCount = 1000;

        static readonly char[] _separators = [',', '、', '，'];
        static readonly StringComparer _tagComparer = StringComparer.OrdinalIgnoreCase;

        List<string> _searchTags = new();
        TagSearchMatch? _searchMatch;
        List<string>? _candidates;

        /// <summary>付いているタグ (Value を区切ったもの)。</summary>
        public List<string> Tags => Split(Value);

        /// <summary>検索で選んだタグ。</summary>
        public List<string> SearchTags => _searchTags.ToList();

        /// <summary>検索の一致 (画面で切り替えていなければデザインの既定)。</summary>
        public TagSearchMatch SearchMatch => _searchMatch ?? Design.SearchMatchDefaultValue;

        [ScriptHide]
        public Func<Task>? OnSearchDataChangedAsync { get; set; }

        /// <summary>タグを足す (同じタグは重ねない。区切りを含めば分けて足す)。</summary>
        [ScriptName("AddTag")]
        public async Task AddTagAsync(string tag) => await SetTagsAsync(Tags.Append(tag));

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            var target = tag.Trim();
            await SetTagsAsync(Tags.Where(e => !_tagComparer.Equals(e, target)));
        }

        /// <summary>そのタグが付いているか (大文字小文字は区別しない)。</summary>
        public bool HasTag(string tag) => Tags.Contains(tag.Trim(), _tagComparer);

        /// <summary>タグを置き換える (入力欄から)。タグが無ければデザインの TextEditEmptyType の値 ("" か null)。</summary>
        [ScriptHide]
        public async Task SetTagsAsync(IEnumerable<string> tags)
        {
            var list = Normalize(tags);
            var text = list.Count == 0
                ? (Design.TextEditEmptyType == TextEditEmptyType.Null ? null : string.Empty)
                : string.Join(Separator, list);
            if (text == Value) return;
            await SetValueAsync(text);
        }

        [ScriptMethodToProperty("SearchTags")]
        public async Task SetSearchTagsAsync(List<string> tags)
        {
            _searchTags = Normalize(tags);
            await AfterSearchParameterChanged();
        }

        [ScriptMethodToProperty("SearchMatch")]
        public async Task SetSearchMatchAsync(TagSearchMatch match)
        {
            _searchMatch = match;
            await AfterSearchParameterChanged();
        }

        //選んだタグごとの Like (部分一致) を SearchMatch で AND / OR。タグを選んでいなければ絞らない
        public MatchConditionBase? GetMatchCondition()
        {
            if (_searchTags.Count == 0) return null;
            var condition = new FieldMatchCondition { FieldName = Design.Name, IsOrMatch = SearchMatch == TagSearchMatch.Any };
            foreach (var tag in _searchTags)
            {
                condition.Children.Add(new FieldValueMatchCondition { SearchTargetVariable = $"{Design.Name}.Value", Comparison = MatchComparison.Like, Value = new StringValue { Value = tag } });
            }
            return condition;
        }

        public async Task SetMatchConditionAsync(FieldMatchCondition condition)
        {
            _searchTags = Normalize(condition.Children.OfType<FieldValueMatchCondition>().Select(e => (e.Value as StringValue)?.Value));
            _searchMatch = condition.IsOrMatch ? TagSearchMatch.Any : TagSearchMatch.All;
            await AfterSearchParameterChanged();
        }

        public async Task ClearMatchConditionAsync()
        {
            _searchTags = new();
            _searchMatch = null;
            await AfterSearchParameterChanged();
        }

        async Task AfterSearchParameterChanged()
        {
            if (OnSearchDataChangedAsync != null) await OnSearchDataChangedAsync();
            await Module.ExecuteScriptAsync(Design.OnSearchDataChanged);
        }

        /// <summary>
        /// 候補のタグ (既に付いているタグを、付いている数の多い順)。最初に呼ばれたときに 1 回だけ読む。
        /// 読むのはタグの列だけで、読める行だけ (通常のモジュールデータ API = 閲覧権限はそのモジュールの設定)。
        /// </summary>
        [ScriptHide]
        public async Task<List<string>> GetCandidatesAsync()
        {
            if (_candidates != null) return _candidates;
            _candidates = new();
            if (Services.AppInfoService.IsDesignMode) return _candidates;

            var hasSource = !string.IsNullOrEmpty(Design.CandidateModuleName);
            var moduleName = hasSource ? Design.CandidateModuleName : Module.Design.Name;
            var fieldName = hasSource && !string.IsNullOrEmpty(Design.CandidateFieldName) ? Design.CandidateFieldName : Design.Name;
            //テーブルを持たないモジュール・読めないモジュールには問い合わせない
            var module = Services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            if (module == null || string.IsNullOrEmpty(module.DbTable) || !module.HasUserReadPermission(Services)) return _candidates;

            var condition = new SearchCondition
            {
                ModuleName = moduleName,
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = $"{fieldName}.Value", Comparison = MatchComparison.NotEqual, Value = new NullValue() }),
                LimitCount = CandidateRowCount,
                SortConditions = new List<SortCondition> { new() { Variable = $"{SystemFieldNames.Id}.Value", IsDescending = true } },
                SelectFields = new List<string> { SystemFieldNames.Id, fieldName },
            };
            var page = (await Services.ModuleDataService.GetListAsync(new List<GetListRequest>
            {
                new() { Condition = condition, PageIndex = 0 },
            })).FirstOrDefault();
            _candidates = (page?.Items ?? new())
                .SelectMany(e => Split((e.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value))
                .GroupBy(e => e, _tagComparer)
                .OrderByDescending(e => e.Count())
                .ThenBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => e.Key)
                .ToList();
            return _candidates;
        }

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && Tags.Count == 0)
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return await base.ValidateInput();
        }

        /// <summary>保存された値をタグに分ける。</summary>
        internal static List<string> Split(string? text) => Normalize(new[] { text });

        /// <summary>区切って前後の空白・空のタグ・重複を落とす (順序は保つ。重複は大文字小文字を区別せず、先の表記を残す)。</summary>
        internal static List<string> Normalize(IEnumerable<string?> texts)
            => texts.SelectMany(e => (e ?? string.Empty).Split(_separators))
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Distinct(_tagComparer)
                .ToList();

        /// <summary>タグの同一判定 (大文字小文字を区別しない)。</summary>
        internal static bool SameTag(string a, string b) => _tagComparer.Equals(a, b);
    }
}
