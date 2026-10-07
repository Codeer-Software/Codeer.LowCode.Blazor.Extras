using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// タグのフィールド。タグ付けモジュールの行を本体の一覧と同じく子として読み・保存する (タグ 1 つ = 行 1 つ、行はタグ名を持つ)。
    /// 何も書かずに足し外しし、レコードの保存で一緒に保存する (1 つのトランザクション・レコードの書き込み権限)。
    /// タグの同一判定は完全一致 (大文字小文字を区別する)。
    /// </summary>
    public class TagField : ListField, ISearchableField
    {
        /// <summary>タグ名の最大の長さ。入力欄・タグ付けモジュールの Name (MaxLength)・DDL の列の長さはこの値にそろえる。</summary>
        public const int MaxTagLength = 200;

        static readonly char[] _separators = [',', '、', '，'];

        TagBinding? _binding;
        bool _bindingResolved;
        readonly TagCandidateProvider _candidates;

        List<string> _searchTags = new();
        TagSearchMatch? _searchMatch;
        MatchConditionBase? _searchCondition;

        public TagField(TagFieldDesign design) : base(design)
        {
            _candidates = new TagCandidateProvider(() => Services, () => Binding);
        }

        /// <summary>このフィールドのデザイン (基底の Design を TagFieldDesign として)。</summary>
        [ScriptHide]
        public TagFieldDesign TagDesign => (TagFieldDesign)Design;

        internal TagBinding? Binding
        {
            get
            {
                if (!_bindingResolved)
                {
                    _binding = TagContracts.Resolve(Services.AppInfoService.GetDesignData(), TagDesign);
                    _bindingResolved = true;
                }
                return _binding;
            }
        }

        /// <summary>付いているタグ (付けた順)。</summary>
        public List<string> Tags => Rows.Select(TagNameOf).Where(e => e.Length > 0).ToList();

        /// <summary>検索で選んだタグ。</summary>
        public List<string> SearchTags => _searchTags.ToList();

        /// <summary>検索の一致 (画面で切り替えていなければデザインの既定)。</summary>
        public TagSearchMatch SearchMatch => _searchMatch ?? TagDesign.SearchMatchDefaultValue;

        /// <summary>そのタグが付いているか (完全一致)。</summary>
        public bool HasTag(string tag) => Tags.Contains(tag.Trim(), StringComparer.Ordinal);

        /// <summary>タグを足す (同じタグは重ねない。区切りを含めば分けて足す)。保存はレコードの保存で。</summary>
        [ScriptName("AddTag")]
        public async Task AddTagAsync(string tag)
        {
            var changed = false;
            foreach (var name in Normalize(new[] { tag })) changed |= await AddOneAsync(name);
            if (changed) AfterTagsChanged();
        }

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            if (await RemoveOneAsync(tag.Trim())) AfterTagsChanged();
        }

        /// <summary>タグを置き換える (入力欄・スクリプトから)。外したタグを外し、無いタグを末尾に足す。</summary>
        [ScriptName("SetTags")]
        public async Task SetTagsAsync(List<string> tags)
        {
            var wanted = Normalize(tags);
            var changed = false;
            foreach (var current in Tags.Where(e => !wanted.Contains(e, StringComparer.Ordinal)).ToList()) changed |= await RemoveOneAsync(current);
            foreach (var name in wanted) changed |= await AddOneAsync(name);
            if (changed) AfterTagsChanged();
        }

        async Task<bool> AddOneAsync(string name)
        {
            var binding = Binding;
            if (binding == null || name.Length == 0 || HasTag(name)) return false;
            if (name.Length > MaxTagLength)
            {
                SetError(string.Format(Properties.Resources.TagFieldTooLongFormat, name[..20], MaxTagLength));
                NotifyStateChanged();
                return false;
            }
            var row = new ModuleData { Name = binding.LinkModule };
            row.Fields[binding.TagNameField] = new TextFieldData { Value = name };
            await AddRowAsync(row);
            return true;
        }

        async Task<bool> RemoveOneAsync(string name)
        {
            var row = Rows.FirstOrDefault(e => TagNameOf(e) == name);
            if (row == null) return false;
            await DeleteRowAsync(row);
            return true;
        }

        //変更通知 (OnDataChanged) は本体の一覧が行の追加・削除で出す
        void AfterTagsChanged()
        {
            ClearError();
            NotifyStateChanged();
        }

        string TagNameOf(Module row) => Binding == null ? string.Empty : row.GetField<TextField>(Binding.TagNameField)?.Value ?? string.Empty;

        #region 読み込み・保存

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            //結び付きが無い (デザインの不備。デザインチェックが指摘する) なら読まない (本体の一覧はモジュール名が空のまま問い合わせてしまう)
            if (Binding == null) return;
            //一覧の行: タグ付け行は一覧の読み込みに同梱されて届く (本体の GetListRelatedData)。
            //タグが 0 件の行には空の一覧が届くので、読み直さない (読み直すと行ごとに問い合わせが出る)
            if (ModuleLayoutType == ModuleLayoutType.List && !Services.AppInfoService.IsDesignMode) AllowLoad = false;
            await base.InitializeDataAsync(fieldDataBase);
        }

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (TagDesign.IsRequired && Tags.Count == 0)
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return await base.ValidateInput();
        }

        #endregion

        /// <summary>打った文字を含む候補 (よく使われている順。サーバーで絞る)。</summary>
        [ScriptHide]
        public Task<List<string>> GetCandidatesAsync(string text) => _candidates.SuggestAsync(text);

        #region 検索

        [ScriptMethodToProperty("SearchTags")]
        public async Task SetSearchTagsAsync(List<string> tags)
        {
            _searchTags = Normalize(tags);
            RebuildSearchCondition();
            await AfterSearchParameterChangedAsync();
        }

        [ScriptMethodToProperty("SearchMatch")]
        public async Task SetSearchMatchAsync(TagSearchMatch match)
        {
            _searchMatch = match;
            RebuildSearchCondition();
            await AfterSearchParameterChangedAsync();
        }

        /// <summary>
        /// 選んだタグの条件 (タグを選んでいなければ絞らない)。子のパスのタグ名に対して、いずれか = In、すべて = ContainsAll (本体が SQL で判定する)。
        /// 条件にはタグ名が入る (画面に戻ったとき・URL から、条件からタグを復元する)。
        /// </summary>
        public new MatchConditionBase? GetMatchCondition() => _searchCondition;

        public new async Task SetMatchConditionAsync(FieldMatchCondition condition)
        {
            var binding = Binding;
            var mine = binding == null ? new List<FieldValueMatchCondition>() : condition.Children.OfType<FieldValueMatchCondition>()
                .Where(e => e.SearchTargetVariable == TagPathVariable(binding))
                .ToList();
            _searchTags = Normalize(mine.SelectMany(e => (e.Value as ListValue<string>)?.Value ?? new List<string>()));
            _searchMatch = mine.Any(e => e.Comparison == MatchComparison.ContainsAll) ? TagSearchMatch.All
                : mine.Count > 0 ? TagSearchMatch.Any
                : null;
            RebuildSearchCondition();
            await AfterSearchParameterChangedAsync();
        }

        public new async Task ClearMatchConditionAsync()
        {
            _searchTags = new();
            _searchMatch = null;
            _searchCondition = null;
            await AfterSearchParameterChangedAsync();
        }

        //親の検索レイアウトに LinkFieldNames で置いたとき (名前が "社員.タグ") も、子のパスは名前の下
        string TagPathVariable(TagBinding binding) => $"{TagDesign.Name}.{binding.TagNameField}.Value";

        void RebuildSearchCondition()
        {
            _searchCondition = null;
            var binding = Binding;
            if (_searchTags.Count == 0 || binding == null) return;
            var any = SearchMatch == TagSearchMatch.Any;
            var group = new FieldMatchCondition { FieldName = TagDesign.Name, IsOrMatch = any };
            group.Children.Add(new FieldValueMatchCondition
            {
                SearchTargetVariable = TagPathVariable(binding),
                Comparison = any ? MatchComparison.In : MatchComparison.ContainsAll,
                Value = MultiTypeValue.Create(_searchTags.ToList()),
            });
            _searchCondition = group;
        }

        async Task AfterSearchParameterChangedAsync()
        {
            if (OnSearchDataChangedAsync != null) await OnSearchDataChangedAsync();
            await Module.ExecuteScriptAsync(TagDesign.OnSearchDataChanged);
        }

        #endregion

        /// <summary>区切って前後の空白・空のタグ・重複を落とす (順序は保つ。重複は完全一致で判定し、先のものを残す)。</summary>
        internal static List<string> Normalize(IEnumerable<string?> texts)
            => texts.SelectMany(e => (e ?? string.Empty).Split(_separators))
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        /// <summary>タグの同一判定 (完全一致)。</summary>
        internal static bool SameTag(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
    }
}
