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
    /// タグ 1 つ = 行 1 つ (リンクの表示文字列がタグ名)。新しいタグはマスタの行として本体の保存に載せる (仮 Id で同じトランザクション)。
    /// 結び付きなしならタグ名をメモリに持つだけ (保存しない。候補はマスタ)。
    /// タグの同一判定は大文字小文字を区別しない (先に入っていた表記を残す)。
    /// </summary>
    public class TagField : ListField, ISearchableField
    {
        //本体が保存前の行に振る仮 Id の形 (保存で実 Id に置き換わる)
        const string TemporaryIdPrefix = "@temporary:";

        static readonly char[] _separators = [',', '、', '，'];
        static readonly StringComparer _tagComparer = StringComparer.OrdinalIgnoreCase;

        readonly List<string> _unboundTags = new();
        readonly List<ModuleData> _stagedTags = new();
        bool _unboundModified;
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
            var changed = false;
            foreach (var name in Normalize(new[] { tag })) changed |= await AddOneAsync(name);
            if (changed) await AfterTagsChangedAsync();
        }

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            if (await RemoveOneAsync(tag.Trim())) await AfterTagsChangedAsync();
        }

        /// <summary>タグを置き換える (入力欄・スクリプトから)。外したタグを外し、無いタグを末尾に足す。</summary>
        [ScriptName("SetTags")]
        public async Task SetTagsAsync(List<string> tags)
        {
            var wanted = Normalize(tags);
            var changed = false;
            foreach (var current in Tags.Where(e => !wanted.Contains(e, _tagComparer)).ToList()) changed |= await RemoveOneAsync(current);
            foreach (var name in wanted) changed |= await AddOneAsync(name);
            if (changed) await AfterTagsChangedAsync();
        }

        async Task<bool> AddOneAsync(string name)
        {
            if (name.Length == 0 || HasTag(name)) return false;
            var entry = await FindTagAsync(name);
            if (!IsBound)
            {
                if (entry == null && !Design.AllowNewTags) return RejectUnknown(name);
                _unboundTags.Add(entry?.Name ?? name);
                _unboundModified = true;
                return true;
            }

            var binding = Binding;
            if (binding == null) return false;
            if (entry == null)
            {
                if (!Design.AllowNewTags) return RejectUnknown(name);
                entry = StageNewTag(binding, name);
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
            //誰も指さなくなった新しいタグは保存しない
            var used = Rows.Select(TagIdOf).ToHashSet();
            _stagedTags.RemoveAll(e => !used.Contains(IdOf(e)));
            return true;
        }

        //マスタに無いタグ: マスタの行を仮 Id で用意し、本体の保存に載せる (GetSubmitData)
        TagEntry StageNewTag(TagBinding binding, string name)
        {
            var staged = _stagedTags.FirstOrDefault(e => _tagComparer.Equals(NameOf(e, binding), name));
            if (staged != null) return new TagEntry(IdOf(staged), NameOf(staged, binding));
            var id = TemporaryIdPrefix + Guid.NewGuid();
            var data = new ModuleData { Name = binding.MasterModule };
            data.Fields[SystemFieldNames.Id] = new IdFieldData { Value = id };
            data.Fields[binding.MasterNameField] = new TextFieldData { Value = name };
            _stagedTags.Add(data);
            return new TagEntry(id, name);
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

        string TagIdOf(Module row) => Binding == null ? string.Empty : row.GetField<LinkField>(Binding.TagLinkField)?.Value ?? string.Empty;

        static string IdOf(ModuleData data) => (data.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;

        static string NameOf(ModuleData data, TagBinding binding) => (data.Fields.GetValueOrDefault(binding.MasterNameField) as TextFieldData)?.Value ?? string.Empty;

        #region 読み込み・保存

        [ScriptHide]
        public override bool IsModified => IsBound ? base.IsModified : _unboundModified;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            //保存の後は読み直しで来る: 足したタグはマスタに入ったので候補を読み直す
            if (_stagedTags.Count > 0) _master = null;
            _stagedTags.Clear();
            _unboundTags.Clear();
            _unboundModified = false;
            //結び付きなしは読むものが無い (本体の一覧はモジュール名が空のまま問い合わせてしまう)
            if (!IsBound) return;
            //一覧の行: 本体の一覧は行ごとに問い合わせる。それを止め、ページの行をまとめて 1 回で読む
            var inListRow = ModuleLayoutType == ModuleLayoutType.List && !Services.AppInfoService.IsDesignMode && Binding != null;
            if (inListRow) AllowLoad = false;
            await base.InitializeDataAsync(fieldDataBase);
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
        public override FieldSubmitData GetSubmitData()
        {
            if (!IsBound) return new();
            var data = base.GetSubmitData();
            //新しいタグのマスタ行はタグ付け行より先に (保存は順に書き、後の行の仮 Id を実 Id に置き換える)
            if (_stagedTags.Count > 0 && data.Add.Count > 0) data.Add.InsertRange(0, _stagedTags);
            return data;
        }

        [ScriptHide]
        public override void AcceptChanges(SubmitAcceptInfo info)
        {
            if (IsBound) base.AcceptChanges(info);
            _unboundModified = false;
            if (_stagedTags.Count == 0) return;
            //足したタグはマスタに入った: 候補を読み直す
            _stagedTags.Clear();
            _master = null;
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

        //名前でタグを引く: 読んである候補 → マスタに直接 (候補は行数で切っているので、そこに無くてもマスタにはあり得る)。
        //DB の = は照合順序しだいで大文字小文字を区別する (SQLite・PostgreSQL) ので、見つからなければ部分一致で引いて手元で比べる
        async Task<TagEntry?> FindTagAsync(string name)
        {
            var hit = (await GetMasterAsync()).FirstOrDefault(e => _tagComparer.Equals(e.Name, name));
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

            var ids = new List<string>();
            var unknown = false;
            foreach (var name in _searchTags)
            {
                var entry = await FindTagAsync(name);
                if (entry == null) unknown = true;
                else ids.Add(entry.Id);
            }

            var any = SearchMatch == TagSearchMatch.Any;
            var group = new FieldMatchCondition { FieldName = Design.Name, IsOrMatch = any };
            group.Children.Add(new FieldValueMatchCondition { SearchTargetVariable = TagPathVariable(binding), Comparison = MatchComparison.In, Value = MultiTypeValue.Create(ids) });
            if (!any && (unknown || ids.Count > 1))
            {
                var owners = unknown ? new List<string>() : await OwnersHavingAllAsync(binding, ids);
                group.Children.Add(new FieldValueMatchCondition { SearchTargetVariable = binding.OwnerKeyVariable, Comparison = MatchComparison.In, Value = MultiTypeValue.Create(owners) });
            }
            _searchCondition = group;
        }

        //選んだタグのタグ付け行を読み、全部のタグが付いているレコードの OwnerId を返す
        async Task<List<string>> OwnersHavingAllAsync(TagBinding binding, List<string> tagIds)
        {
            var distinct = tagIds.Distinct().ToList();
            var condition = new SearchCondition
            {
                ModuleName = binding.LinkModule,
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = $"{binding.TagLinkField}.Value", Comparison = MatchComparison.In, Value = MultiTypeValue.Create(distinct) }),
                SortConditions = new List<SortCondition> { new() { Variable = $"{SystemFieldNames.Id}.Value" } },
                SelectFields = new List<string> { SystemFieldNames.Id, binding.OwnerIdField, binding.TagLinkField },
            };
            var rows = await TagContracts.ReadAllAsync((c, page) => TagListBatchLoader.ReadPageAsync(Services, c, page), condition, 5000);
            return rows
                .GroupBy(e => TagContracts.OwnerId(e, binding))
                .Where(g => g.Key.Length > 0 && g.Select(e => (e.Fields.GetValueOrDefault(binding.TagLinkField) as LinkFieldData)?.Value).Distinct().Count() == distinct.Count)
                .Select(g => g.Key)
                .ToList();
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
