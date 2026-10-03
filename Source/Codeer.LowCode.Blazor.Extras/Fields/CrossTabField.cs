using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// クロス集計フィールド。元モジュールの行を 行の項目 × 列の項目 で集計して表にする。
    /// 集計はサーバーの集計 API (権限は一覧と同じ) で行い、行ごとの合計・列ごとの合計・総計は項目を減らした集計を別に実行して得る (CrossTabBuilder)。
    /// 一覧ページの表示 (ISearchResultsViewField) にもなれる: 検索条件がそのまま集計の条件になる。
    /// 集計定義の出どころは 設計 (Setting) か、スクリプトの Show(ModuleAggregator) で渡された定義。表はどちらも同じように描く。
    /// </summary>
    public class CrossTabField : FieldBase<CrossTabFieldDesign>, ISearchResultsViewField
    {
        //画面に描く表のセル数 (行の種類 × 列の種類 × 値の数) の上限。超えたら表を描かずに「行や列を減らす」案内を出す (ブラウザで描ける大きさの目安)
        internal const int MaxCellCount = 20000;

        SearchCondition? _additionalCondition;
        AggregateCondition? _scriptCondition;
        CrossTabSetting? _userSetting;
        CrossTabValueDisplay? _userValueDisplay;
        bool _userSettingApplied;
        Func<Task> _showCustomDialog = () => Task.CompletedTask;
        List<AggregateGroup> _rows;
        List<AggregateGroup> _columns;
        //今の表を作った集計定義 (セルの条件を作るのに使う。時差も入っている)
        AggregateCondition? _tableCondition;

        [ScriptHide]
        public Func<SearchCondition?, Task> OnQueryChangedAsync { get; set; } = _ => Task.CompletedTask;
        [ScriptHide]
        public Func<CrossTabCell, Task> OnCellClickAsync { get; set; } = _ => Task.CompletedTask;

        public CrossTabField(CrossTabFieldDesign design) : base(design)
        {
            _rows = design.Setting.Rows.Select(e => Clone(e)).ToList();
            _columns = design.Setting.Columns.Select(e => Clone(e)).ToList();
        }

        /// <summary>読み込みを止める (条件を組み立ててから Reload で読む等)。</summary>
        public bool AllowLoad { get; set; } = true;

        /// <summary>集計している元モジュール (スクリプトの定義があればそのモジュール)。</summary>
        [ScriptHide]
        public string ModuleName => _scriptCondition?.ModuleName ?? Design.SearchCondition.ModuleName;

        [ScriptHide]
        public SearchField? SearchField { get; set; }

        /// <summary>今の表 (読み込み前・失敗時は null)。</summary>
        public CrossTab? Table { get; private set; }

        /// <summary>読み込みに失敗したときのメッセージ (成功なら空)。</summary>
        public string LoadError { get; private set; } = string.Empty;

        public bool IsLoading { get; private set; }

        /// <summary>値の表示形式 (値 / 総計・行の合計・列の合計 に対する割合)。利用者のカスタマイズがあればそれ、無ければ設計。</summary>
        [ScriptHide]
        public CrossTabValueDisplay ValueDisplay => _scriptCondition == null ? _userValueDisplay ?? Design.ValueDisplay : Design.ValueDisplay;

        /// <summary>今使っている集計の設定 (利用者のカスタマイズがあればそれ、無ければ設計)。スクリプトの Show で渡した定義は含まない。</summary>
        [ScriptHide]
        public CrossTabSetting CurrentSetting => _userSetting ?? Design.Setting;

        /// <summary>利用者がカスタマイズできる状態か (設計で許可・元モジュールあり・スクリプトの Show で定義を渡していない・実行時)。</summary>
        [ScriptHide]
        public bool CanCustomize => Design.CanCustomize && _scriptCondition == null
            && !string.IsNullOrEmpty(Design.SearchCondition.ModuleName) && !Services.AppInfoService.IsDesignMode;

        /// <summary>利用者のカスタマイズが効いているか。</summary>
        [ScriptHide]
        public bool IsCustomized => _userSetting != null || _userValueDisplay != null;

        /// <summary>行に置いた項目 (設計の Rows、または Show で渡した定義の先頭 rowCount 個)。</summary>
        [ScriptHide]
        public List<AggregateGroup> RowGroups => _rows;

        /// <summary>列に置いた項目。</summary>
        [ScriptHide]
        public List<AggregateGroup> ColumnGroups => _columns;

        public override bool IsModified => false;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            if (!this.IsInLayout()) return;
            //カスタマイズできる表は、画面がブラウザの保存内容を読んで ApplyUserSettingAsync を呼ぶまで待つ (設計の設定で 1 回集計してから読み直さない)
            if (Design.CanCustomize && !_userSettingApplied && !Services.AppInfoService.IsDesignMode) return;
            await ReloadAsync();
        }

        /// <summary>利用者の設定 (保存内容を今の設計に合わせたもの。null なら設計どおり) を使って集計し直す。画面が呼ぶ。</summary>
        internal async Task ApplyUserSettingAsync(CrossTabSetting? setting, CrossTabValueDisplay? valueDisplay)
        {
            _userSetting = setting;
            _userValueDisplay = valueDisplay;
            _userSettingApplied = true;
            if (_scriptCondition == null)
            {
                _rows = CurrentSetting.Rows.Select(Clone).ToList();
                _columns = CurrentSetting.Columns.Select(Clone).ToList();
            }
            await ReloadAsync();
        }

        internal void SetShowCustomDialog(Func<Task> show) => _showCustomDialog = show;

        /// <summary>集計のカスタマイズのダイアログを開く (設計で「利用者が集計を変更できる」がオンのときだけ開く)。</summary>
        [ScriptName("ShowCustomDialog")]
        public async Task ShowCustomDialogAsync()
        {
            if (!CanCustomize) return;
            await _showCustomDialog();
        }

        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        /// <summary>スクリプトで組んだ集計定義を表示する。グループは全部が行になる (列なし)。</summary>
        [ScriptName("Show")]
        public async Task ShowAsync(ModuleAggregator aggregator) => await ShowAsync(aggregator, -1);

        /// <summary>スクリプトで組んだ集計定義を表示する。グループの先頭 rowCount 個を行に、残りを列に置く。設計の定義より優先し、追加の条件 (SetAdditionalCondition) は AND になる。</summary>
        [ScriptName("Show")]
        public async Task ShowAsync(ModuleAggregator aggregator, int rowCount)
        {
            var condition = aggregator.GetAggregateCondition();
            var count = rowCount < 0 ? condition.Groups.Count : Math.Min(rowCount, condition.Groups.Count);
            _rows = condition.Groups.Take(count).Select(Clone).ToList();
            _columns = condition.Groups.Skip(count).Select(Clone).ToList();
            _scriptCondition = condition;
            await ReloadAsync();
        }

        /// <summary>追加の条件 (一覧の検索条件やスクリプトの ModuleSearcher)。設計の条件と AND になる。</summary>
        [ScriptName("SetAdditionalCondition")]
        public async Task SetAdditionalConditionAsync(ModuleSearcher searcher)
            => await SetAdditionalConditionAsync(searcher.GetSearchCondition(), 0);

        [ScriptHide]
        public async Task SetAdditionalConditionAsync(SearchCondition condition, int page)
        {
            if (condition.ModuleName != ModuleName)
                throw LowCodeException.Create("{0} Invalid Module", ModuleName, condition.ModuleName);
            _additionalCondition = condition;
            await OnQueryChangedAsync(GetSearchCondition());
            await ReloadAsync();
        }

        [ScriptHide]
        public override async Task OnExternalFieldChangedAsync(string fieldName)
        {
            if (!this.IsInLayout()) return;
            var searchCondition = GetSearchCondition();
            if (searchCondition.GetFieldVariableConditions().All(e => new VariableName(e.Variable).FieldName.Root != fieldName)) return;
            await ReloadAsync();
        }

        /// <summary>集計し直す。</summary>
        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (!AllowLoad) return;
            if (Services.AppInfoService.IsDesignMode)
            {
                Table = string.IsNullOrEmpty(ModuleName) ? null : CreateDesignSample();
                NotifyStateChanged();
                return;
            }
            if (string.IsNullOrEmpty(ModuleName)) return;
            var condition = GetAggregateCondition();
            if (condition.Measures.Count == 0) return;
            //UTC 保存の日時は見ている人 (ブラウザ) の時差で区切る
            if (condition.UtcOffsetMinutes == null) condition.UseLocalTimeZone();
            IsLoading = true;
            LoadError = string.Empty;
            NotifyStateChanged();
            try
            {
                var totals = Design.ShowRowTotals || Design.ShowColumnTotals || Design.ShowGrandTotal || ValueDisplay != CrossTabValueDisplay.Value;
                Table = await CrossTabBuilder.BuildAsync(condition, _rows.Count, Services.ModuleDataService.AggregateAsync, totals, MaxCellCount);
                _tableCondition = condition;
            }
            catch (Exception e)
            {
                Table = null;
                _tableCondition = null;
                LoadError = e.Message;
            }
            finally
            {
                IsLoading = false;
                NotifyStateChanged();
            }
        }

        /// <summary>今の集計定義 (設計 または Show で渡した定義。条件は追加の条件と AND)。</summary>
        [ScriptHide]
        public AggregateCondition GetAggregateCondition()
        {
            var condition = _scriptCondition == null ? Design.CreateAggregateCondition(CurrentSetting, _rows, _columns) : CloneCondition(_scriptCondition);
            condition.Condition = GetSearchCondition().Condition;
            return condition;
        }

        [ScriptHide]
        public async Task InvokeCellClickAsync(CrossTabCell cell)
        {
            if (!string.IsNullOrEmpty(Design.OnCellClick)) await Module.ExecuteScriptAsync(Design.OnCellClick, cell);
            await OnCellClickAsync(cell);
        }

        /// <summary>
        /// 今の表のセルに数えた行の条件 (rowIndex / columnIndex が null なら その向きの合計)。表の条件 + 行・列の鍵で、鍵は軸の型が作る (日付は期間の範囲・空値は空値の行)。
        /// 集計後の絞り込み・上限で行 (行の軸が無ければ列) を選んでいる表の合計は、表に出ている項目の分だけ (合計の値と同じ範囲)。表が無ければ null。
        /// </summary>
        internal MatchConditionBase? CreateCellCondition(int? rowIndex, int? columnIndex)
        {
            var table = Table;
            var source = _tableCondition;
            if (table == null || source == null) return null;
            var designData = Services.AppInfoService.GetDesignData();
            var module = designData.Modules.Find(source.ModuleName);
            if (module == null) return null;
            var offset = source.UtcOffsetMinutes ?? 0;

            MatchConditionBase? Keys(CrossTabAxisValue axis, List<AggregateGroup> groups)
            {
                var parts = new List<MatchConditionBase>();
                for (var i = 0; i < axis.Keys.Count && i < groups.Count; i++)
                {
                    var field = CrossTabFieldDesign.ResolveField(designData, module, groups[i].Variable);
                    if (field == null) return null;
                    parts.Add(groups[i].CreateKeyCondition(axis.Keys[i].Value, field, offset));
                }
                return parts.Count switch { 0 => null, 1 => parts[0], _ => MultiMatchCondition.And(parts.ToArray()) };
            }
            MatchConditionBase? AnyOf(List<CrossTabAxisValue> axes, List<AggregateGroup> groups)
            {
                var parts = axes.Select(a => Keys(a, groups)).ToList();
                return parts.Count == 0 || parts.Any(e => e == null) ? null : MultiMatchCondition.Or(parts.ToArray()!);
            }

            var conditions = new List<MatchConditionBase>();
            if (source.Condition != null) conditions.Add(source.Condition.JsonClone());
            if (rowIndex != null && Keys(table.Rows[rowIndex.Value], _rows) is { } row) conditions.Add(row);
            if (columnIndex != null && Keys(table.Columns[columnIndex.Value], _columns) is { } column) conditions.Add(column);
            var cuts = source.Having.Count > 0 || source.LimitCount != null;
            if (cuts)
            {
                if (_rows.Count > 0 && rowIndex == null && AnyOf(table.Rows, _rows) is { } shownRows) conditions.Add(shownRows);
                else if (_rows.Count == 0 && columnIndex == null && AnyOf(table.Columns, _columns) is { } shownColumns) conditions.Add(shownColumns);
            }
            return conditions.Count == 0 ? null : MultiMatchCondition.And(conditions.ToArray());
        }

        //条件の元: 設計の検索条件、または Show で渡した定義の条件
        SearchCondition GetSearchCondition()
        {
            var baseCondition = _scriptCondition == null
                ? Design.SearchCondition
                : new SearchCondition(_scriptCondition.ModuleName) { Condition = _scriptCondition.Condition };
            return baseCondition.MergeSearchCondition(_additionalCondition);
        }

        static AggregateGroup Clone(AggregateGroup g) => g.JsonClone();

        static AggregateCondition CloneCondition(AggregateCondition src)
        {
            var dst = new AggregateCondition(src.ModuleName) { Condition = src.Condition, LimitCount = src.LimitCount, UtcOffsetMinutes = src.UtcOffsetMinutes };
            dst.Groups.AddRange(src.Groups);
            dst.Measures.AddRange(src.Measures);
            dst.Having.AddRange(src.Having);
            dst.SortConditions.AddRange(src.SortConditions);
            return dst;
        }

        //デザイナのプレビュー用の表 (3 × 3)。見出しは設定した軸から作る: 日付・日時は丸め単位に合わせたダミーの期間、それ以外は「項目の表示名 1 / 2 / 3」(実データと紛れない)。数字はダミー
        CrossTab CreateDesignSample()
        {
            var measures = Design.Setting.Measures.Count == 0 ? [new AggregateMeasure { Function = AggregateFunction.Count }] : Design.Setting.Measures;
            var table = new CrossTab { Measures = measures.ToList(), HasTotals = true, TotalCount = 90, GroupCount = 9 };
            const int size = 3;
            var rowKeys = _rows.Select(SampleKeys).ToList();
            var columnKeys = _columns.Select(SampleKeys).ToList();
            var columnCount = _columns.Count == 0 ? 1 : size;
            for (var i = 0; i < size; i++)
                table.Rows.Add(new CrossTabAxisValue { Keys = rowKeys.Select(k => new AggregateKey { Value = MultiTypeValue.Create(k[i]), DisplayText = k[i] }).ToList() });
            for (var i = 0; i < columnCount; i++)
                table.Columns.Add(new CrossTabAxisValue { Keys = columnKeys.Select(k => new AggregateKey { Value = MultiTypeValue.Create(k[i]), DisplayText = k[i] }).ToList() });
            for (var m = 0; m < measures.Count; m++)
            {
                var cells = new List<List<MultiTypeValue>>();
                for (var r = 0; r < size; r++)
                    cells.Add(Enumerable.Range(0, columnCount).Select(c => MultiTypeValue.Create((decimal)((r + 1) * 10 + c * 5))).ToList());
                table.Cells.Add(cells);
                table.RowTotals.Add(cells.Select(row => MultiTypeValue.Create(row.Sum(v => (decimal)v.GetValue()!))).ToList());
                table.ColumnTotals.Add(Enumerable.Range(0, columnCount).Select(c => MultiTypeValue.Create(cells.Sum(row => (decimal)row[c].GetValue()!))).ToList());
                table.GrandTotals.Add(MultiTypeValue.Create(cells.Sum(row => row.Sum(v => (decimal)v.GetValue()!))));
            }
            return table;
        }

        //軸 1 つ分のプレビュー見出し 3 つ
        string[] SampleKeys(AggregateGroup group)
        {
            var designData = Services.AppInfoService.GetDesignData();
            var module = designData.Modules.Find(ModuleName);
            var field = module == null ? null : CrossTabFieldDesign.ResolveField(designData, module, group.Variable);
            if (field is DateFieldDesign or DateTimeFieldDesign)
            {
                var dateGroup = group as DateGroup;
                var fiscal = dateGroup?.FiscalYearStartMonth ?? 1;
                return dateGroup?.Bucket switch
                {
                    DateBucket.Year => [CrossTabKeyText.Year(2025, fiscal), CrossTabKeyText.Year(2026, fiscal), CrossTabKeyText.Year(2027, fiscal)],
                    DateBucket.Quarter => [CrossTabKeyText.Quarter(2026, 1, fiscal), CrossTabKeyText.Quarter(2026, 2, fiscal), CrossTabKeyText.Quarter(2026, 3, fiscal)],
                    DateBucket.Month => ["2026-01", "2026-02", "2026-03"],
                    DateBucket.Week => ["2026-01-05", "2026-01-12", "2026-01-19"],
                    DateBucket.Hour => ["2026-01-01 09:00", "2026-01-01 10:00", "2026-01-01 11:00"],
                    _ => ["2026-01-01", "2026-01-02", "2026-01-03"],
                };
            }
            var name = field == null ? new VariableName(group.Variable).FieldName.FullName : Services.AppInfoService.Localize(CrossTabFieldDesign.DisplayTextOf(field));
            if (string.IsNullOrEmpty(name)) name = "?";
            return [$"{name} 1", $"{name} 2", $"{name} 3"];
        }
    }
}
