using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// タグのフィールド。結び付きあり (検索条件 = タグ付けモジュール) なら本体の一覧と同じくタグ付け行を子として読み・保存し、
    /// タグ 1 つ = 行 1 つ (リンクの表示文字列がタグ名)。マスタに無いタグは、足したときにマスタに行を作る
    /// (レコードの保存に載せると、同じ新しいタグを足した複数のレコードを一緒に保存したときにマスタの行が重なるため)。
    /// 結び付きなしならタグ名をメモリに持つだけ (保存しない。候補はマスタ。マスタには書かない)。
    /// タグの同一判定は大文字小文字を区別しない (先に入っていた表記を残す)。
    /// </summary>
    public class TagField : ListField, ISearchableField
    {
        //本体が保存前の行に振る仮 Id の形 (保存で実 Id に置き換わる)
        const string TemporaryIdPrefix = "@temporary:";

        static readonly char[] _separators = [',', '、', '，'];
        static readonly StringComparer _tagComparer = StringComparer.OrdinalIgnoreCase;

        readonly List<string> _unboundTags = new();
        bool _unboundModified;
        //このレコードのタグ付け行を読んだか (詳細・新規・一覧の行のまとめ読み・LoadTags)
        bool _rowsLoaded;
        TagBinding? _binding;
        bool _bindingResolved;
        Task<List<TagEntry>>? _master;

        List<string> _searchTags = new();
        TagSearchMatch? _searchMatch;
        MatchConditionBase? _searchCondition;

        public TagField(TagFieldDesign design) : base(design) { }

        [ScriptHide]
        public new TagFieldDesign Design => (TagFieldDesign)base.Design;

        /// <summary>タグ付けモジュールと結び付いているか (false = 保存しない入力欄)。</summary>
        [ScriptHide]
        public bool IsBound => !string.IsNullOrEmpty(Design.SearchCondition?.ModuleName);

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
        public List<string> Tags => IsBound ? Rows.Select(TagNameOf).Where(e => e.Length > 0).ToList() : _unboundTags.ToList();

        /// <summary>検索で選んだタグ。</summary>
        public List<string> SearchTags => _searchTags.ToList();

        /// <summary>検索の一致 (画面で切り替えていなければデザインの既定)。</summary>
        public TagSearchMatch SearchMatch => _searchMatch ?? Design.SearchMatchDefaultValue;

        /// <summary>そのタグが付いているか (大文字小文字は区別しない)。</summary>
        public bool HasTag(string tag) => Tags.Contains(tag.Trim(), _tagComparer);

        /// <summary>タグを足す (同じタグは重ねない。区切りを含めば分けて足す)。マスタに無いタグは AllowNewTags なら保存のときマスタに足す。</summary>
        [ScriptName("AddTag")]
        public async Task AddTagAsync(string tag)
        {
            await LoadTagsAsync();
            var changed = false;
            foreach (var name in Normalize(new[] { tag })) changed |= await AddOneAsync(name);
            if (changed) await AfterTagsChangedAsync();
        }

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            await LoadTagsAsync();
            if (await RemoveOneAsync(tag.Trim())) await AfterTagsChangedAsync();
        }

        /// <summary>
        /// このレコードのタグを読む (まだ読んでいなければ)。詳細画面・一覧の行・新規のレコードは読み込み済み。
        /// スクリプトの ModuleSearcher で読んだレコードは子の一覧を読まないので、Tags を見る前に呼ぶ (AddTag / RemoveTag / SetTags は自分で呼ぶ)。
        /// </summary>
        [ScriptName("LoadTags")]
        public async Task LoadTagsAsync()
        {
            if (_rowsLoaded || !IsBound || Binding == null) return;
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
            if (changed) await AfterTagsChangedAsync();
        }

        async Task<bool> AddOneAsync(string name)
        {
            if (name.Length == 0 || HasTag(name)) return false;
            if (!IsBound)
            {
                //マスタに無いタグも入れてよいなら問い合わせない (読んである候補があれば表記だけ寄せる)。一覧の行の表示用にたくさん並ぶため
                var known = Design.AllowNewTags ? FindLoadedTag(name) : await FindTagAsync(name);
                if (known == null && !Design.AllowNewTags) return RejectUnknown(name);
                _unboundTags.Add(known?.Name ?? name);
                _unboundModified = true;
                return true;
            }

            var binding = Binding;
            if (binding == null) return false;
            var entry = await FindTagAsync(name);
            if (entry == null)
            {
                if (!Design.AllowNewTags) return RejectUnknown(name);
                entry = await CreateTagAsync(binding, name);
                if (entry == null) return false;
            }
            var row = new ModuleData { Name = binding.LinkModule };
            row.Fields[binding.TagLinkField] = new LinkFieldData { Value = entry.Id, DisplayText = entry.Name };
            await AddRowAsync(row);
            return true;
        }

        async Task<bool> RemoveOneAsync(string name)
        {
            if (!IsBound)
            {
                if (_unboundTags.RemoveAll(e => _tagComparer.Equals(e, name)) == 0) return false;
                _unboundModified = true;
                return true;
            }
            var row = Rows.FirstOrDefault(e => _tagComparer.Equals(TagNameOf(e), name));
            if (row == null) return false;
            await DeleteRowAsync(row);
            return true;
        }

        //マスタに無いタグ: マスタに行を作る (ほかのレコード・ほかの人が同時に作っていたら、そちらを使う)
        async Task<TagEntry?> CreateTagAsync(TagBinding binding, string name)
        {
            if (Services.AppInfoService.IsDesignMode) return null;
            var id = TemporaryIdPrefix + Guid.NewGuid();
            var data = new ModuleData { Name = binding.MasterModule };
            data.Fields[SystemFieldNames.Id] = new IdFieldData { Value = id };
            data.Fields[binding.MasterNameField] = new TextFieldData { Value = name };
            var result = (await Services.ModuleDataService.SubmitAsync(new List<ModuleSubmitData> { new() { ModuleName = binding.MasterModule, Add = { data } } }))?.FirstOrDefault();
            if (result != null && string.IsNullOrEmpty(result.ExceptionMessage) && result.TemporaryIdMap.TryGetValue(id, out var realId))
            {
                var created = new TagEntry(realId, name);
                if (_master is { IsCompletedSuccessfully: true }) _master.Result.Add(created);
                return created;
            }
            //一意インデックスで止まった = 先に誰かが作った
            var existing = await FindTagAsync(name);
            if (existing != null) return existing;
            SetError(result?.ExceptionMessage ?? string.Format(Properties.Resources.TagFieldUnknownTagFormat, name));
            NotifyStateChanged();
            return null;
        }

        bool RejectUnknown(string name)
        {
            SetError(string.Format(Properties.Resources.TagFieldUnknownTagFormat, name));
            NotifyStateChanged();
            return false;
        }

        async Task AfterTagsChangedAsync()
        {
            ClearError();
            //結び付きありの変更通知 (OnDataChanged) は本体の一覧が行の追加・削除で出す
            if (!IsBound) await Module.ExecuteScriptAsync(Design.OnDataChanged);
            NotifyStateChanged();
        }

        string TagNameOf(Module row) => Binding == null ? string.Empty : row.GetField<LinkField>(Binding.TagLinkField)?.DisplayText ?? string.Empty;

        static string IdOf(ModuleData data) => (data.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;

        static string NameOf(ModuleData data, TagBinding binding) => (data.Fields.GetValueOrDefault(binding.MasterNameField) as TextFieldData)?.Value ?? string.Empty;

        #region 読み込み・保存

        [ScriptHide]
        public override bool IsModified => IsBound ? base.IsModified : _unboundModified;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _unboundTags.Clear();
            _unboundModified = false;
            //結び付きなしは読むものが無い (本体の一覧はモジュール名が空のまま問い合わせてしまう)
            if (!IsBound) return;
            //一覧の行: 本体の一覧は行ごとに問い合わせる。それを止め、ページの行をまとめて 1 回で読む
            var inListRow = ModuleLayoutType == ModuleLayoutType.List && !Services.AppInfoService.IsDesignMode && Binding != null;
            if (inListRow) AllowLoad = false;
            await base.InitializeDataAsync(fieldDataBase);
            //本体の一覧が読むのは詳細だけ (スクリプトの ModuleSearcher で読んだレコードは読まない → LoadTags)
            _rowsLoaded = ModuleLayoutType == ModuleLayoutType.Detail || Module.IsNewData;
            if (inListRow) ListLoad = TagListBatchLoader.Register(this);
        }

        [ScriptHide]
        public override FieldDataBase? GetData() => IsBound ? base.GetData() : new ListFieldData();

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase)
        {
            if (IsBound) await base.SetDataAsync(fieldDataBase);
        }

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => IsBound ? base.GetSubmitData() : new();

        [ScriptHide]
        public override void AcceptChanges(SubmitAcceptInfo info)
        {
            if (IsBound) base.AcceptChanges(info);
            _unboundModified = false;
        }

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && Tags.Count == 0)
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return !IsBound || await base.ValidateInput();
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

        #region 候補 (マスタ)

        internal sealed record TagEntry(string Id, string Name);

        /// <summary>候補のタグ (マスタの名前順、CandidateRowCount 行まで)。最初に呼ばれたときに 1 回だけ読む。</summary>
        [ScriptHide]
        public async Task<List<string>> GetCandidatesAsync() => (await GetMasterAsync()).Select(e => e.Name).ToList();

        Task<List<TagEntry>> GetMasterAsync() => _master ??= ReadMasterAsync();

        async Task<List<TagEntry>> ReadMasterAsync()
        {
            var binding = Binding;
            if (binding == null || Services.AppInfoService.IsDesignMode || !CanReadMaster(binding)) return new();
            return await QueryMasterAsync(binding, null, Design.CandidateRowCount);
        }

        //読んである候補からだけ引く (問い合わせない)
        TagEntry? FindLoadedTag(string name)
            => _master is { IsCompletedSuccessfully: true } ? _master.Result.FirstOrDefault(e => _tagComparer.Equals(e.Name, name)) : null;

        //名前でタグを引く: 読んである候補 → マスタに直接 (候補はまだ読んでいないことも、行数で切れていることもある。候補のためだけに大きく読まない)。
        //DB の = は照合順序しだいで大文字小文字を区別する (SQLite・PostgreSQL) ので、見つからなければ部分一致で引いて手元で比べる
        async Task<TagEntry?> FindTagAsync(string name)
        {
            var hit = FindLoadedTag(name);
            if (hit != null) return hit;
            var binding = Binding;
            if (binding == null || Services.AppInfoService.IsDesignMode || !CanReadMaster(binding)) return null;
            foreach (var comparison in new[] { MatchComparison.Equal, MatchComparison.Like })
            {
                var condition = new FieldValueMatchCondition { SearchTargetVariable = $"{binding.MasterNameField}.Value", Comparison = comparison, Value = new StringValue { Value = name } };
                hit = (await QueryMasterAsync(binding, condition, 1000)).FirstOrDefault(e => _tagComparer.Equals(e.Name, name));
                if (hit != null) return hit;
            }
            return null;
        }

        //Id でタグを引く (検索条件の復元)。読んである候補に無いものだけマスタに問い合わせる
        async Task<List<TagEntry>> FindTagsByIdAsync(List<string> ids)
        {
            var master = await GetMasterAsync();
            var found = master.Where(e => ids.Contains(e.Id)).ToList();
            var missing = ids.Where(id => found.All(e => e.Id != id)).ToList();
            var binding = Binding;
            if (missing.Count > 0 && binding != null && !Services.AppInfoService.IsDesignMode && CanReadMaster(binding))
            {
                var condition = new FieldValueMatchCondition { SearchTargetVariable = $"{SystemFieldNames.Id}.Value", Comparison = MatchComparison.In, Value = MultiTypeValue.Create(missing) };
                found.AddRange(await QueryMasterAsync(binding, condition, missing.Count));
            }
            return ids.Select(id => found.FirstOrDefault(e => e.Id == id)).OfType<TagEntry>().ToList();
        }

        bool CanReadMaster(TagBinding binding)
            => Services.AppInfoService.GetDesignData().Modules.Find(binding.MasterModule)?.HasUserReadPermission(Services) == true;

        async Task<List<TagEntry>> QueryMasterAsync(TagBinding binding, MatchConditionBase? condition, int limit)
        {
            var search = new SearchCondition
            {
                ModuleName = binding.MasterModule,
                Condition = condition == null ? new MultiMatchCondition() : MultiMatchCondition.And(condition),
                LimitCount = limit,
                SortConditions = new List<SortCondition> { new() { Variable = $"{binding.MasterNameField}.Value" } },
                SelectFields = new List<string> { SystemFieldNames.Id, binding.MasterNameField },
            };
            var page = (await Services.ModuleDataService.GetListAsync(new List<GetListRequest> { new() { Condition = search, PageIndex = 0 } })).FirstOrDefault();
            return (page?.Items ?? new())
                .Select(e => new TagEntry(IdOf(e), NameOf(e, binding)))
                .Where(e => e.Id.Length > 0 && e.Name.Length > 0)
                .ToList();
        }

        #endregion

        #region 検索

        [ScriptMethodToProperty("SearchTags")]
        public async Task SetSearchTagsAsync(List<string> tags)
        {
            _searchTags = Normalize(tags);
            await RebuildSearchConditionAsync();
            await AfterSearchParameterChangedAsync();
        }

        [ScriptMethodToProperty("SearchMatch")]
        public async Task SetSearchMatchAsync(TagSearchMatch match)
        {
            _searchMatch = match;
            await RebuildSearchConditionAsync();
            await AfterSearchParameterChangedAsync();
        }

        /// <summary>
        /// 選んだタグの条件 (タグを選んでいなければ絞らない)。いずれか = 子のパスでタグ Id の In。
        /// すべて = それに加えて、全部のタグを持つレコードの Id の In (子は 1 回しか結合されないので、AND を子のパスでは書けない)。
        /// タグ Id は条件に残す (画面に戻ったとき条件からタグを復元する)。マスタに無いタグはどのレコードにも合わない。
        /// </summary>
        public new MatchConditionBase? GetMatchCondition() => _searchCondition;

        public new async Task SetMatchConditionAsync(FieldMatchCondition condition)
        {
            var binding = Binding;
            var tagIds = binding == null ? new List<string>() : condition.Children.OfType<FieldValueMatchCondition>()
                .Where(e => e.SearchTargetVariable == TagPathVariable(binding))
                .SelectMany(e => (e.Value as ListValue<string>)?.Value ?? new List<string>())
                .ToList();
            _searchTags = (await FindTagsByIdAsync(tagIds)).Select(e => e.Name).ToList();
            _searchMatch = condition.IsOrMatch ? TagSearchMatch.Any : TagSearchMatch.All;
            await RebuildSearchConditionAsync();
            await AfterSearchParameterChangedAsync();
        }

        public new async Task ClearMatchConditionAsync()
        {
            _searchTags = new();
            _searchMatch = null;
            _searchCondition = null;
            await AfterSearchParameterChangedAsync();
        }

        string TagPathVariable(TagBinding binding) => $"{Design.Name}.{binding.TagLinkField}.Value";

        async Task RebuildSearchConditionAsync()
        {
            _searchCondition = null;
            var binding = Binding;
            if (_searchTags.Count == 0 || binding == null || !binding.IsBound) return;

            //打ったタグごとのタグ Id (部分一致なら、その文字を含むタグ全部)。どれかが 0 件なら「すべて含む」は誰にも合わない
            var terms = new List<List<string>>();
            var unknown = false;
            foreach (var name in _searchTags)
            {
                var found = Design.PartialMatch
                    ? (await FindTagsLikeAsync(binding, name)).Select(e => e.Id).Distinct().ToList()
                    : (await FindTagAsync(name)) is { } one ? new List<string> { one.Id } : new List<string>();
                if (found.Count == 0) unknown = true;
                else terms.Add(found);
            }
            var ids = terms.SelectMany(e => e).Distinct().ToList();

            var any = SearchMatch == TagSearchMatch.Any;
            var group = new FieldMatchCondition { FieldName = Design.Name, IsOrMatch = any };
            group.Children.Add(new FieldValueMatchCondition { SearchTargetVariable = TagPathVariable(binding), Comparison = MatchComparison.In, Value = MultiTypeValue.Create(ids) });
            if (!any && (unknown || terms.Count > 1))
            {
                var owners = unknown ? new List<string>() : await OwnersHavingAllAsync(binding, terms);
                //親の検索レイアウトに LinkFieldNames で置いたとき (名前が "社員.タグ") は、レコードの Id も同じパスの下 ("社員.Id.Value")
                var prefix = Design.Name.Contains('.') ? Design.Name[..(Design.Name.LastIndexOf('.') + 1)] : string.Empty;
                group.Children.Add(new FieldValueMatchCondition { SearchTargetVariable = prefix + binding.OwnerKeyVariable, Comparison = MatchComparison.In, Value = MultiTypeValue.Create(owners) });
            }
            _searchCondition = group;
        }

        //選んだタグのタグ付け行を読み、打ったタグごとにそのどれかが付いているレコード (= 全部を持つレコード) の OwnerId を返す。
        //丸ごと一致ならタグごとに 1 つの Id、部分一致ならその文字を含むタグ Id の組
        async Task<List<string>> OwnersHavingAllAsync(TagBinding binding, List<List<string>> terms)
        {
            var all = terms.SelectMany(e => e).Distinct().ToList();
            var condition = new SearchCondition
            {
                ModuleName = binding.LinkModule,
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = $"{binding.TagLinkField}.Value", Comparison = MatchComparison.In, Value = MultiTypeValue.Create(all) }),
                SortConditions = new List<SortCondition> { new() { Variable = $"{SystemFieldNames.Id}.Value" } },
                SelectFields = new List<string> { SystemFieldNames.Id, binding.OwnerIdField, binding.TagLinkField },
            };
            var rows = await TagContracts.ReadAllAsync((c, page) => TagListBatchLoader.ReadPageAsync(Services, c, page), condition, 5000);
            return rows
                .GroupBy(e => TagContracts.OwnerId(e, binding))
                .Where(g =>
                {
                    if (g.Key.Length == 0) return false;
                    var mine = g.Select(e => (e.Fields.GetValueOrDefault(binding.TagLinkField) as LinkFieldData)?.Value ?? string.Empty).ToHashSet();
                    return terms.All(term => term.Any(mine.Contains));
                })
                .Select(g => g.Key)
                .ToList();
        }

        //打った文字を含むタグ (部分一致の検索用。マスタに 1 回問い合わせる。大文字小文字は手元で区別しない)
        async Task<List<TagEntry>> FindTagsLikeAsync(TagBinding binding, string text)
        {
            if (Services.AppInfoService.IsDesignMode || !CanReadMaster(binding)) return new();
            var condition = new FieldValueMatchCondition { SearchTargetVariable = $"{binding.MasterNameField}.Value", Comparison = MatchComparison.Like, Value = new StringValue { Value = text } };
            return (await QueryMasterAsync(binding, condition, 1000)).Where(e => e.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
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
