using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// タグのフィールド。タグ付けモジュールの行を本体の一覧と同じく子として読み・保存する (タグ 1 つ = 行 1 つ、行はタグ名を持つ)。
    /// 何も書かずに足し外しし、レコードの保存で一緒に保存する (1 つのトランザクション・レコードの書き込み権限)。
    /// タグの同一判定は大文字小文字を区別しない (同じレコードの中は先の表記を残す。新しく足すタグは、既に使われている表記があればそれに寄せる)。
    /// </summary>
    public class TagField : ListField, ISearchableField
    {
        static readonly char[] _separators = [',', '、', '，'];
        static readonly StringComparer _tagComparer = StringComparer.OrdinalIgnoreCase;

        //このレコードのタグ付け行を読んだか (詳細・新規・一覧の行のまとめ読み・LoadTags)
        bool _rowsLoaded;
        TagBinding? _binding;
        bool _bindingResolved;
        readonly TagCandidateProvider _candidates;

        List<string> _searchTags = new();
        TagSearchMatch? _searchMatch;
        MatchConditionBase? _searchCondition;

        public TagField(TagFieldDesign design) : base(design)
        {
            _candidates = TagCandidateProvider.Create(() => Services, design, () => Binding);
        }

        //本体側の口 C (ListField の Design・検索まわりを virtual に) ができるまで、同名で隠す。口ができたら override にする
        [ScriptHide]
        public new TagFieldDesign Design => (TagFieldDesign)base.Design;

        internal TagBinding? Binding
        {
            get
            {
                if (!_bindingResolved)
                {
                    _binding = TagContracts.Resolve(Services.AppInfoService.GetDesignData(), Design);
                    _bindingResolved = true;
                }
                return _binding;
            }
        }

        /// <summary>一覧の行でまとめ読みを待っている間の処理 (テスト・デバッグ用)。</summary>
        internal Task? ListLoad { get; private set; }

        /// <summary>付いているタグ (付けた順)。</summary>
        public List<string> Tags => Rows.Select(TagNameOf).Where(e => e.Length > 0).ToList();

        /// <summary>検索で選んだタグ。</summary>
        public List<string> SearchTags => _searchTags.ToList();

        /// <summary>検索の一致 (画面で切り替えていなければデザインの既定)。</summary>
        public TagSearchMatch SearchMatch => _searchMatch ?? Design.SearchMatchDefaultValue;

        /// <summary>そのタグが付いているか (大文字小文字は区別しない)。</summary>
        public bool HasTag(string tag) => Tags.Contains(tag.Trim(), _tagComparer);

        /// <summary>タグを足す (同じタグは重ねない。区切りを含めば分けて足す)。保存はレコードの保存で。</summary>
        [ScriptName("AddTag")]
        public async Task AddTagAsync(string tag)
        {
            await LoadTagsAsync();
            var changed = false;
            foreach (var name in Normalize(new[] { tag })) changed |= await AddOneAsync(name);
            if (changed) AfterTagsChanged();
        }

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            await LoadTagsAsync();
            if (await RemoveOneAsync(tag.Trim())) AfterTagsChanged();
        }

        /// <summary>
        /// このレコードのタグを読む (まだ読んでいなければ)。詳細画面・一覧の行・新規のレコードは読み込み済み。
        /// スクリプトの ModuleSearcher で読んだレコードは子の一覧を読まないので、Tags を見る前に呼ぶ (AddTag / RemoveTag / SetTags は自分で呼ぶ)。
        /// </summary>
        [ScriptName("LoadTags")]
        public async Task LoadTagsAsync()
        {
            if (_rowsLoaded || Binding == null) return;
            _rowsLoaded = true;
            if (Module.IsNewData) return;
            AllowLoad = true;
            await ReloadAsync();
        }

        /// <summary>タグを置き換える (入力欄・スクリプトから)。外したタグを外し、無いタグを末尾に足す。</summary>
        [ScriptName("SetTags")]
        public async Task SetTagsAsync(List<string> tags)
        {
            await LoadTagsAsync();
            var wanted = Normalize(tags);
            var changed = false;
            foreach (var current in Tags.Where(e => !wanted.Contains(e, _tagComparer)).ToList()) changed |= await RemoveOneAsync(current);
            foreach (var name in wanted) changed |= await AddOneAsync(name);
            if (changed) AfterTagsChanged();
        }

        async Task<bool> AddOneAsync(string name)
        {
            var binding = Binding;
            if (binding == null || name.Length == 0 || HasTag(name)) return false;
            //既に使われている表記に寄せる (無ければ打ったまま)。決まったタグだけなら、候補に無いタグは足さない
            var spelling = await _candidates.FindSpellingAsync(name, query: true);
            if (spelling == null && !Design.AllowNewTags) return RejectUnknown(name);
            var row = new ModuleData { Name = binding.LinkModule };
            row.Fields[binding.TagNameField] = new TextFieldData { Value = spelling ?? name };
            await AddRowAsync(row);
            return true;
        }

        async Task<bool> RemoveOneAsync(string name)
        {
            var row = Rows.FirstOrDefault(e => _tagComparer.Equals(TagNameOf(e), name));
            if (row == null) return false;
            await DeleteRowAsync(row);
            return true;
        }

        bool RejectUnknown(string name)
        {
            SetError(string.Format(Properties.Resources.TagFieldUnknownTagFormat, name));
            NotifyStateChanged();
            return false;
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
            //一覧の行: 本体の一覧は行ごとに問い合わせる。それを止め、ページの行をまとめて 1 回で読む (本体側の口 B ができるまで)
            var inListRow = ModuleLayoutType == ModuleLayoutType.List && !Services.AppInfoService.IsDesignMode;
            if (inListRow) AllowLoad = false;
            await base.InitializeDataAsync(fieldDataBase);
            //本体の一覧が読むのは詳細だけ (スクリプトの ModuleSearcher で読んだレコードは読まない → LoadTags)
            _rowsLoaded = ModuleLayoutType == ModuleLayoutType.Detail || Module.IsNewData;
            if (inListRow) ListLoad = TagListBatchLoader.Register(this);
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

        /// <summary>一覧の行のまとめ読みの結果を受け取る (TagListBatchLoader から)。</summary>
        internal async Task ApplyListRowsAsync(List<ModuleData> rows)
        {
            //その前にスクリプトが LoadTags / AddTag で読んでいれば、そちらが新しい
            if (_rowsLoaded) return;
            _rowsLoaded = true;
            await ApplyDataAsync(new Paging<ModuleData> { Items = rows, TotalCount = rows.Count });
            NotifyStateChanged();
        }

        /// <summary>このレコードの、タグ付け行の OwnerId に入る値 (通常は Id)。</summary>
        internal string OwnerKey
        {
            get
            {
                var field = Binding == null ? string.Empty : TagContracts.FieldOfVariable(Binding.OwnerKeyVariable);
                return (Module.GetField(field)?.GetData() as ValueFieldDataBase<string>)?.Value ?? string.Empty;
            }
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
        /// 本体側の口 C (ListField の検索まわりを virtual に) ができるまで、ListField の同名のメソッドを隠す。口ができたら override にする。
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
        string TagPathVariable(TagBinding binding) => $"{Design.Name}.{binding.TagNameField}.Value";

        void RebuildSearchCondition()
        {
            _searchCondition = null;
            var binding = Binding;
            if (_searchTags.Count == 0 || binding == null) return;
            var any = SearchMatch == TagSearchMatch.Any;
            var group = new FieldMatchCondition { FieldName = Design.Name, IsOrMatch = any };
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
            await Module.ExecuteScriptAsync(Design.OnSearchDataChanged);
        }

        #endregion

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
