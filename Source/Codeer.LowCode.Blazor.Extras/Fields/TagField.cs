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
    /// タグのフィールド。タグ付けモジュールの行 (タグ 1 つ = 行 1 つ、行はタグ名を持つ) を従属レコードとして読み・保存する。
    /// 何も書かずに足し外しし、レコードの保存で一緒に保存する (1 つのトランザクション・レコードの書き込み権限)。
    /// タグの同一判定は完全一致 (大文字小文字を区別する)。
    /// </summary>
    public class TagField(TagFieldDesign design)
        : FieldBase<TagFieldDesign>(design), ISearchableField, IOwnedRecordsField
    {
        /// <summary>タグ名の最大の長さ。入力欄・タグ付けモジュールの Name (MaxLength)・DDL の列の長さはこの値にそろえる。</summary>
        public const int MaxTagLength = 200;

        //候補の数 (設定にはしない)
        const int CandidateLimit = 10;

        static readonly char[] _separators = [',', '、', '，'];

        private readonly ModuleCollection _modules = new();

        //このレコードのタグ付け行を読んだか (同梱された・読み込んだ・渡された行に差し替えた)。保存済みのレコードで読んでいなければ、書く前に 1 回読む
        bool _loaded;

        //タグ付けモジュールの契約と結び付き (最初に使うときに 1 回解決する。フィールドの間、デザインは変わらない)
        (TagLinkContractFieldDesign? Link, SearchCondition? Condition)? _binding;

        List<string> _searchTags = new();
        TagSearchMatch? _searchMatch;
        MatchConditionBase? _searchCondition;

        //IOwnedRecordsField: 行の出し入れと、与えられた行の表示。行の突き合わせ・内容の反映は本体 (Module.ApplyRecordAsync) が行う

        //全件を持っている (HoldsAllRecords) ので、読んであれば今の行を返す
        async Task<IReadOnlyList<Module>> IOwnedRecordsField.LoadOwnedRecordsAsync(string name)
        {
            if (name != Design.Name) return [];
            await EnsureLoadedAsync();
            return _modules.Items;
        }

        //宣言 (GetOwnedRecords) は結び付きがあるときだけなので、ここでは結び付きがある
        async Task<Module> IOwnedRecordsField.AddOwnedRecordAsync(string name, string? id)
            => await OwnedRecordModules.AddAsync(this, _modules, Condition!, string.Empty, id, ModuleLayoutType.None);

        Task IOwnedRecordsField.RemoveOwnedRecordAsync(string name, Module record)
        {
            if (name == Design.Name) _modules.Remove(record);
            return Task.CompletedTask;
        }

        async Task IOwnedRecordsField.RefreshOwnedRecordsAsync(string name)
        {
            if (name != Design.Name) return;
            await InvokeOnDataChangedAsync();
            NotifyStateChanged();
        }

        //与えられたタグ付け行をそのまま表示する (DB は読まない・表示専用)
        async Task IOwnedRecordsField.ShowOwnedRecordsAsync(string name, IReadOnlyList<OwnedRecordRow> rows)
        {
            if (name != Design.Name) return;
            _modules.ApplyLoaded(await OwnedRecordModules.CreateForShowAsync(this, string.Empty, rows, ModuleLayoutType.None));
            _loaded = true;
            NotifyStateChanged();
        }

        public bool AllowLoad { get; set; } = true;

        public override bool IsModified => _modules.IsModified;

        /// <summary>付いているタグ (付けた順)。</summary>
        public List<string> Tags => _modules.Items.Select(TagNameOf).Where(e => e.Length > 0).ToList();

        /// <summary>検索で選んだタグ。</summary>
        public List<string> SearchTags => _searchTags.ToList();

        /// <summary>検索の一致 (画面で切り替えていなければデザインの既定)。</summary>
        public TagSearchMatch SearchMatch => _searchMatch ?? Design.SearchMatchDefaultValue;

        /// <summary>そのタグが付いているか (完全一致)。</summary>
        public bool HasTag(string tag)
        {
            var name = tag.Trim();
            return _modules.Items.Any(e => SameTag(TagNameOf(e), name));
        }

        /// <summary>タグを足す (同じタグは重ねない。区切りを含めば分けて足す)。保存はレコードの保存で。</summary>
        [ScriptName("AddTag")]
        public async Task AddTagAsync(string tag)
        {
            ClearError();
            await EnsureLoadedAsync();
            var changed = false;
            foreach (var name in Normalize(new[] { tag })) changed |= await AddOneAsync(name);
            await AfterTagsChangedAsync(changed);
        }

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            ClearError();
            await EnsureLoadedAsync();
            await AfterTagsChangedAsync(RemoveOne(tag.Trim()));
        }

        /// <summary>タグを置き換える (入力欄・スクリプトから)。外したタグを外し、無いタグを末尾に足す。</summary>
        [ScriptName("SetTags")]
        public async Task SetTagsAsync(List<string> tags)
        {
            ClearError();
            await EnsureLoadedAsync();
            var wanted = Normalize(tags);
            var changed = false;
            foreach (var current in Tags.Where(e => !wanted.Contains(e, StringComparer.Ordinal)).ToList()) changed |= RemoveOne(current);
            foreach (var name in wanted) changed |= await AddOneAsync(name);
            await AfterTagsChangedAsync(changed);
        }

        //足せなかったタグのエラーは、同じ呼び出しでほかのタグが足せても残す (次の足し外しで消える)
        async Task<bool> AddOneAsync(string name)
        {
            var (link, condition) = Binding;
            if (link == null || condition == null || name.Length == 0 || HasTag(name)) return false;
            if (name.Length > MaxTagLength)
            {
                SetError(string.Format(Properties.Resources.TagFieldTooLongFormat, name[..20], MaxTagLength));
                return false;
            }
            var row = await OwnedRecordModules.AddAsync(this, _modules, condition, string.Empty, null, ModuleLayoutType.None);
            await row.GetField<TextField>(link.TagName)!.SetValueAsync(name);
            return true;
        }

        bool RemoveOne(string name)
        {
            var row = _modules.Items.FirstOrDefault(e => SameTag(TagNameOf(e), name));
            if (row == null) return false;
            _modules.Remove(row);
            return true;
        }

        async Task AfterTagsChangedAsync(bool changed)
        {
            if (changed) await InvokeOnDataChangedAsync();
            NotifyStateChanged();
        }

        async Task InvokeOnDataChangedAsync()
        {
            await NotifyDataChangedAsync();
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
        }

        //契約と、このレコードのタグ付け行の条件 (デザインがタグ付けモジュールの契約から組み立てる)。契約が無ければどちらも null
        (TagLinkContractFieldDesign? Link, SearchCondition? Condition) Binding
            => _binding ??= ResolveBinding(Services.AppInfoService.GetDesignData().Modules);

        (TagLinkContractFieldDesign? Link, SearchCondition? Condition) ResolveBinding(IModuleDesigns modules)
            => (TagContracts.LinkContract(modules.Find(Design.TagModuleName)), Design.GetChildRecordsCondition(modules));

        TagLinkContractFieldDesign? Link => Binding.Link;

        SearchCondition? Condition => Binding.Condition;

        string TagNameOf(Module row)
        {
            var link = Link;
            return link == null ? string.Empty : row.GetField<TextField>(link.TagName)?.Value ?? string.Empty;
        }

        #region 読み込み・保存

        /// <summary>
        /// 一覧の行・スクリプトで読んだレコード: 一覧の読み込みに同梱された行を使う (無ければ表示のためには読まない。行ごとの問い合わせは出さない)。
        /// 詳細: このレコードのタグ付け行を 1 回読む。
        /// 未保存のレコード (コピーなど) に渡された行は、このレコードの行ではない (未保存の親に紐づく子は無い): タグを新しい行として足す。
        /// </summary>
        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _modules.ApplyLoaded([]);
            _loaded = false;
            var data = fieldDataBase as ListFieldData;
            var rows = data?.GetModules() ?? new();
            //未保存のレコードは DB に行が無いので読まない (後からデータを入れられて保存済みになったら、書く前に読む)
            if (Module.IsNewData)
            {
                if (rows.Count > 0) await AddAsNewAsync(data!);
                return;
            }
            if (rows.Count > 0 || (data != null && ModuleLayoutType != ModuleLayoutType.Detail))
            {
                await ApplyRowsAsync(rows);
                return;
            }
            if (ModuleLayoutType == ModuleLayoutType.Detail && AllowLoad && !Services.AppInfoService.IsDesignMode && this.IsInLayout()) await LoadAsync();
        }

        //書く前に今のタグ付け行を知る: 読んでいない保存済みのレコードだけ 1 回読む (知らずに足すと同じタグの行が二重になる)
        async Task EnsureLoadedAsync()
        {
            if (_loaded || Module.IsNewData || !AllowLoad || Services.AppInfoService.IsDesignMode) return;
            await LoadAsync();
        }

        //このレコードのタグ付け行を読む (タグ付けモジュールが無い・契約が無いときは読むものが無い)
        async Task LoadAsync()
        {
            _loaded = true;
            if (Condition is { } condition) _modules.ApplyLoaded(await this.GetChildModulesAsync(condition, ModuleLayoutType.None));
        }

        async Task ApplyRowsAsync(IEnumerable<ModuleData> rows)
        {
            var modules = new List<Module>();
            foreach (var row in rows) modules.Add(await ModuleCreationService.CreateModuleAsync(Services, row, ModuleLayoutType.None));
            _modules.ApplyLoaded(modules);
            _loaded = true;
        }

        //渡された行のタグ名を、このレコードの新しいタグ付け行にする (付いていた行は捨てる)
        async Task AddAsNewAsync(ListFieldData data)
        {
            _modules.ApplyLoaded([]);
            _loaded = true;
            foreach (var name in Normalize(TagContracts.TagNames(Services.AppInfoService.GetDesignData(), Design, data))) await AddOneAsync(name);
            NotifyStateChanged();
        }

        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
            => Condition is { } condition ? _modules.GetSubmitData(this, condition) : new();

        [ScriptHide]
        public override void AcceptChanges(SubmitAcceptInfo info)
            => _modules.AcceptChanges(info);

        /// <summary>タグ付け行 (意味検索の文章など、レコードのデータからタグ名を読む側のため)。</summary>
        [ScriptHide]
        public override FieldDataBase? GetData() => new ListFieldData { Children = _modules.Items.Select(e => e.GetData()).ToList() };

        /// <summary>
        /// 渡されたタグ付け行に差し替える (一覧の行の読み直しなど)。未保存のレコード (コピーなど) に渡された行はこのレコードの行ではないので、タグ名だけを新しい行にする。
        /// 行のデータが無ければ (同梱されていない) 変えない。
        /// </summary>
        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase)
        {
            if (fieldDataBase is not ListFieldData data) return;
            if (Module.IsNewData)
            {
                if (data.GetModules().Count > 0) await AddAsNewAsync(data);
                return;
            }
            await ApplyRowsAsync(data.GetModules());
            NotifyStateChanged();
        }

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && Tags.Count == 0)
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            if (!await _modules.ValidateInput()) return false;
            return await base.ValidateInput();
        }

        #endregion

        /// <summary>
        /// 打った文字を含む候補 (よく使われている順。サーバーで絞る): タグ付けモジュールを
        /// 「タグ名でグループ化・件数・件数の多い順 → 名前順・上位 10 件・打った文字で部分一致」で 1 回引く (本体の集計)。
        /// 効くのはタグ付けモジュールの読み取り条件 (UserRead / DataRead)。タグを付けるモジュール (親) の行の条件は効かない。
        /// </summary>
        [ScriptHide]
        public async Task<List<string>> GetCandidatesAsync(string text)
        {
            text = text.Trim();
            var link = Link;
            if (text.Length == 0 || link == null || Services.AppInfoService.IsDesignMode) return new();
            var module = Services.AppInfoService.GetDesignData().Modules.Find(Design.TagModuleName);
            if (module == null || !module.HasUserReadPermission(Services)) return new();

            var variable = $"{link.TagName}.Value";
            var aggregate = new AggregateCondition(Design.TagModuleName)
            {
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = variable, Comparison = MatchComparison.Like, Value = new StringValue { Value = text } }),
                Groups = { new ValueGroup { Variable = variable } },
                Measures = { new AggregateMeasure { Function = AggregateFunction.Count, Name = "count" } },
                SortConditions =
                {
                    new AggregateSort { Target = AggregateSortTarget.Measure, Index = 0, IsDescending = true },
                    new AggregateSort { Target = AggregateSortTarget.Group, Index = 0 },
                },
                LimitCount = CandidateLimit,
            };
            try
            {
                var result = (await Services.ModuleDataService.AggregateAsync(new List<AggregateCondition> { aggregate })).FirstOrDefault();
                return (result?.Rows ?? new())
                    .Select(e => e.KeyText(0) ?? string.Empty)
                    .Where(e => e.Length > 0)
                    .ToList();
            }
            catch (Exception e)
            {
                await Services.Logger.Error(e.Message);
                return new();
            }
        }

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
        [ScriptHide]
        public MatchConditionBase? GetMatchCondition() => _searchCondition;

        [ScriptHide]
        public async Task SetMatchConditionAsync(FieldMatchCondition condition)
        {
            var variable = TagPathVariable();
            var mine = variable == null ? new List<FieldValueMatchCondition>() : condition.Children.OfType<FieldValueMatchCondition>()
                .Where(e => e.SearchTargetVariable == variable)
                .ToList();
            _searchTags = Normalize(mine.SelectMany(e => (e.Value as ListValue<string>)?.Value ?? new List<string>()));
            _searchMatch = mine.Any(e => e.Comparison == MatchComparison.ContainsAll) ? TagSearchMatch.All
                : mine.Count > 0 ? TagSearchMatch.Any
                : null;
            RebuildSearchCondition();
            await AfterSearchParameterChangedAsync();
        }

        [ScriptHide]
        public async Task ClearMatchConditionAsync()
        {
            _searchTags = new();
            _searchMatch = null;
            _searchCondition = null;
            await AfterSearchParameterChangedAsync();
        }

        //親の検索レイアウトに LinkFieldNames で置いたとき (名前が "社員.タグ") も、子のパスは名前の下
        string? TagPathVariable()
        {
            var link = Link;
            return link == null ? null : $"{Design.Name}.{link.TagName}.Value";
        }

        void RebuildSearchCondition()
        {
            _searchCondition = null;
            var variable = TagPathVariable();
            if (_searchTags.Count == 0 || variable == null) return;
            var any = SearchMatch == TagSearchMatch.Any;
            var group = new FieldMatchCondition { FieldName = Design.Name, IsOrMatch = any };
            group.Children.Add(new FieldValueMatchCondition
            {
                SearchTargetVariable = variable,
                Comparison = any ? MatchComparison.In : MatchComparison.ContainsAll,
                Value = MultiTypeValue.Create(_searchTags.ToList()),
            });
            _searchCondition = group;
        }

        Task AfterSearchParameterChangedAsync()
        {
            NotifyStateChanged();
            return Task.CompletedTask;
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
