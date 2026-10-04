using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// クロス集計。元モジュール (SearchCondition) の行を、行の項目 × 列の項目 で件数・合計・平均などに集計して表にする。
    /// 集計は本体の集計 API (ModuleDataIO.AggregateAsync / IModuleDataService.AggregateAsync) で行い、権限は一覧と同じ (読めない行は数えない・読めない項目は使えない)。
    /// 検索フィールドの結果の表示先 (ISearchResultsViewFieldDesign) になれるので、検索条件で絞った行だけを集計できる。
    /// 元モジュールを空にしておき、スクリプトの Show(ModuleAggregator) で集計定義を渡して表示することもできる (表はその定義を描くだけ)。
    /// </summary>
    [Designer(DisplayName = "$CrossTabField")]
    [ToolboxIcon(PackIconMaterialKind = "TableLarge")]
    [IgnoreBaseProperties(nameof(IgnoreModification), nameof(OnValidateInput))]
    public class CrossTabFieldDesign : FieldDesignBase, IDisplayName, ISearchResultsViewFieldDesign, IFillHeightFieldDesign
    {
        /// <summary>デザインチェック指摘の番号 (固定。追加は末尾・欠番は再利用しない)。</summary>
        public static class Codes
        {
            public const int NoMeasure = 1;
            public const int InvalidFunction = 2;
            public const int DateBucketRequiresDate = 3;
            public const int IndexOutOfRange = 4;
            public const int UnknownField = 5;
        }

        public CrossTabFieldDesign() : base(typeof(CrossTabFieldDesign).FullName!) { }

        [Designer(Index = 1, Scope = DesignerScope.All, DisplayName = "$SearchCondition")]
        public SearchCondition SearchCondition { get; set; } = new();

        [Designer(Index = 2, Scope = DesignerScope.All, DisplayName = "$DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [Designer(Index = 3, Scope = DesignerScope.All, DisplayName = "$CrossTabSetting")]
        [CustomPropertyControl]
        public CrossTabSetting Setting { get; set; } = new();

        [Designer(Index = 4, Scope = DesignerScope.All, DisplayName = "$ShowRowTotals")]
        public bool ShowRowTotals { get; set; } = true;

        [Designer(Index = 5, Scope = DesignerScope.All, DisplayName = "$ShowColumnTotals")]
        public bool ShowColumnTotals { get; set; } = true;

        [Designer(Index = 6, Scope = DesignerScope.All, DisplayName = "$ShowGrandTotal")]
        public bool ShowGrandTotal { get; set; } = true;

        [Designer(Index = 7, Scope = DesignerScope.All, DisplayName = "$CrossTabValueDisplay")]
        public CrossTabValueDisplay ValueDisplay { get; set; } = CrossTabValueDisplay.Value;

        /// <summary>
        /// 利用者が集計 (行・列・値・値で絞り込み・並べ替え・表示件数の上限・値の表示形式) を自分用に変えられる。
        /// 表の右上の「集計のカスタマイズ」ボタン かスクリプトの ShowCustomDialog() で開き、ブラウザ (localStorage) に保存する。ListField のカラムカスタマイズと同じ作り
        /// </summary>
        [Designer(Index = 9, Scope = DesignerScope.All, DisplayName = "$CrossTabCanCustomize")]
        public bool CanCustomize { get; set; }

        /// <summary>セルをクリックしたとき。引数はクリックしたセル (行・列の鍵と値)。明細の一覧へ遷移するなどに使う。</summary>
        [Designer(Index = 10, Scope = DesignerScope.All, CandidateType = CandidateType.ScriptEvent, DisplayName = "$OnCellClick"),
         ScriptMethod(ArgumentTypes = ["CrossTabCell"], ArgumentNames = ["cell"])]
        public string OnCellClick { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(CrossTabFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldDataBase? CreateData() => null;
        public override FieldBase CreateField() => new CrossTabField(this);

        /// <summary>設定を集計定義にする (条件は SearchCondition のもの。追加の条件は実行側が足す)。</summary>
        internal AggregateCondition CreateAggregateCondition(IEnumerable<AggregateGroup>? rows = null, IEnumerable<AggregateGroup>? columns = null)
            => CreateAggregateCondition(Setting, rows, columns);

        /// <summary>指定した設定 (利用者のカスタマイズなど) を集計定義にする。元モジュールと条件はこの設計のもの。</summary>
        internal AggregateCondition CreateAggregateCondition(CrossTabSetting setting, IEnumerable<AggregateGroup>? rows = null, IEnumerable<AggregateGroup>? columns = null)
        {
            var condition = new AggregateCondition(SearchCondition.ModuleName) { Condition = SearchCondition.Condition };
            condition.Groups.AddRange(rows ?? setting.Rows);
            condition.Groups.AddRange(columns ?? setting.Columns);
            condition.Measures.AddRange(setting.Measures);
            condition.Having.AddRange(setting.Having);
            condition.SortConditions.AddRange(setting.SortConditions);
            condition.LimitCount = setting.LimitCount;
            return condition;
        }

        /// <summary>
        /// 設定の不整合 (値が無い・使えない集計方法・日付でない項目の日付の単位・範囲外の番号) を (番号, 文言) で返す。デザインチェックと利用者のカスタマイズで共用する。
        /// includeUnknownField: 元モジュールに無い項目も返す (デザインチェックは本体の存在確認が別に出すので false)
        /// </summary>
        internal List<(int Code, string Message)> ValidateSetting(CrossTabSetting setting, DesignData designData, bool includeUnknownField)
        {
            var result = new List<(int Code, string Message)>();
            var module = designData.Modules.Find(SearchCondition.ModuleName);
            FieldDesignBase? Resolve(string variable) => module == null ? null : ResolveField(designData, module, variable);

            foreach (var group in setting.Rows.Concat(setting.Columns))
            {
                var field = Resolve(group.Variable);
                if (field == null)
                {
                    if (includeUnknownField) result.Add((Codes.UnknownField, string.Format(Resources.CrossTab_UnknownField, group.Variable)));
                    continue;
                }
                if (group is DateGroup && !group.CanApplyTo(field))
                    result.Add((Codes.DateBucketRequiresDate, string.Format(Resources.CrossTab_DateBucketRequiresDate, group.Variable)));
            }
            if (setting.Measures.Count == 0) result.Add((Codes.NoMeasure, Resources.CrossTab_NoMeasure));
            foreach (var measure in setting.Measures)
            {
                if (measure.Function == AggregateFunction.Count) continue;
                var field = Resolve(measure.Variable);
                if (field == null)
                {
                    if (includeUnknownField) result.Add((Codes.UnknownField, string.Format(Resources.CrossTab_UnknownField, measure.Variable)));
                    continue;
                }
                if (!measure.CanApplyTo(field)) result.Add((Codes.InvalidFunction, string.Format(Resources.CrossTab_InvalidFunction, measure.Function, measure.Variable)));
            }
            var groupCount = setting.Rows.Count + setting.Columns.Count;
            foreach (var h in setting.Having)
            {
                if (h.MeasureIndex < 0 || setting.Measures.Count <= h.MeasureIndex)
                    result.Add((Codes.IndexOutOfRange, string.Format(Resources.CrossTab_IndexOutOfRange, h.MeasureIndex)));
            }
            foreach (var s in setting.SortConditions)
            {
                var count = s.Target == AggregateSortTarget.Group ? groupCount : setting.Measures.Count;
                if (s.Index < 0 || count <= s.Index)
                    result.Add((Codes.IndexOutOfRange, string.Format(Resources.CrossTab_IndexOutOfRange, s.Index)));
            }
            return result;
        }

        /// <summary>
        /// 保存してあった利用者の設定を今の設計に合わせる (ListField のカラムカスタマイズと同じく黙って合わせる)。
        /// 元モジュールに無い項目を指す行・列・値は捨て、値を捨てたぶん値で絞り込み・並べ替えの番号を詰め、指す先が無くなったものは捨てる。値が 1 つも残らなければ null (設計の設定で表示する)。
        /// </summary>
        internal CrossTabSetting? ReconcileSetting(CrossTabSetting saved, DesignData designData)
        {
            var module = designData.Modules.Find(SearchCondition.ModuleName);
            if (module == null) return null;
            bool Exists(string variable) => ResolveField(designData, module, variable) != null;

            var result = new CrossTabSetting { LimitCount = saved.LimitCount };
            //まとめる項目の番号は 行 → 列 の通し番号。残った行・列で付け直す
            var groupMap = new Dictionary<int, int>();
            var keptRows = Enumerable.Range(0, saved.Rows.Count).Where(i => Exists(saved.Rows[i].Variable)).ToList();
            var keptColumns = Enumerable.Range(0, saved.Columns.Count).Where(i => Exists(saved.Columns[i].Variable)).ToList();
            foreach (var i in keptRows)
            {
                groupMap[i] = result.Rows.Count;
                result.Rows.Add(saved.Rows[i].JsonClone());
            }
            foreach (var i in keptColumns)
            {
                groupMap[saved.Rows.Count + i] = keptRows.Count + result.Columns.Count;
                result.Columns.Add(saved.Columns[i].JsonClone());
            }

            var measureMap = new Dictionary<int, int>();
            for (var i = 0; i < saved.Measures.Count; i++)
            {
                var m = saved.Measures[i];
                if (m.Function != AggregateFunction.Count && !Exists(m.Variable)) continue;
                measureMap[i] = result.Measures.Count;
                result.Measures.Add(new AggregateMeasure { Function = m.Function, Variable = m.Variable, Name = m.Name, Format = m.Format });
            }
            if (result.Measures.Count == 0) return null;
            foreach (var h in saved.Having)
            {
                if (!measureMap.TryGetValue(h.MeasureIndex, out var index)) continue;
                result.Having.Add(new AggregateHaving { MeasureIndex = index, Comparison = h.Comparison, Value = h.Value });
            }
            foreach (var s in saved.SortConditions)
            {
                var map = s.Target == AggregateSortTarget.Group ? groupMap : measureMap;
                if (!map.TryGetValue(s.Index, out var index)) continue;
                result.SortConditions.Add(new AggregateSort { Target = s.Target, Index = index, IsDescending = s.IsDescending });
            }
            return result;
        }

        /// <summary>利用者のカスタマイズで選べる項目: 元モジュールの値の項目と、リンク先 1 段の値の項目 (変数名と見出し)。</summary>
        internal List<(string Variable, string Text, bool IsDate)> GetFieldCandidates(DesignData designData, Func<string, string> localize)
        {
            var list = new List<(string Variable, string Text, bool IsDate)>();
            var module = designData.Modules.Find(SearchCondition.ModuleName);
            if (module == null) return list;
            static bool IsScalar(FieldDesignBase f) => f is DbValueFieldDesignBase && f is not IListFieldDesign && f.Name.IndexOf('.') < 0;
            static bool IsDate(FieldDesignBase f) => f is DateFieldDesign or DateTimeFieldDesign;
            foreach (var field in module.Fields.Where(IsScalar))
            {
                var text = localize(DisplayTextOf(field));
                list.Add(($"{field.Name}.Value", text, IsDate(field)));
                var target = LinkTargetModuleName(field);
                if (string.IsNullOrEmpty(target) || designData.Modules.Find(target) is not { } targetModule) continue;
                foreach (var linked in targetModule.Fields.Where(IsScalar))
                    list.Add(($"{field.Name}.{linked.Name}.Value", $"{text} / {localize(DisplayTextOf(linked))}", IsDate(linked)));
            }
            return list;
        }

        /// <summary>項目の見出し (表示名があれば表示名、無ければ項目名)。</summary>
        internal static string DisplayTextOf(FieldDesignBase field)
            => field is IDisplayName d && !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : field.Name;

        /// <summary>
        /// リンク越しの項目をたどるときの相手のモジュール名 (リンクでなければ null)。
        /// 本体の集計が解決するリンクと同じ範囲: LinkField・ModuleField・モジュール参照の SelectField。
        /// </summary>
        internal static string? LinkTargetModuleName(FieldDesignBase field) => field switch
        {
            LinkFieldDesign l => l.SearchCondition.ModuleName,
            ModuleFieldDesign m => m.ModuleName,
            SelectFieldDesign s when !string.IsNullOrEmpty(s.SearchCondition.ModuleName) => s.SearchCondition.ModuleName,
            _ => null,
        };

        /// <summary>変数名から元モジュールの項目を引く。リンク越し ("Customer.Region.Value") はリンク (LinkField / ModuleField / モジュール参照の SelectField) をたどる。無ければ null。</summary>
        internal static FieldDesignBase? ResolveField(DesignData designData, ModuleDesign module, string variable)
        {
            if (string.IsNullOrEmpty(variable)) return null;
            var fieldName = new VariableName(variable).FieldName;
            var current = module;
            while (true)
            {
                var exact = current.Fields.FirstOrDefault(e => e.Name == fieldName.FullName);
                if (exact != null || !fieldName.IsLink) return exact;
                var link = current.Fields.FirstOrDefault(e => e.Name == fieldName.Root);
                var target = link == null ? null : LinkTargetModuleName(link);
                if (string.IsNullOrEmpty(target) || designData.Modules.Find(target) is not { } next) return null;
                current = next;
                fieldName = fieldName.SkipRoot();
            }
        }

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            result.AddRange(SearchCondition.CheckDesign(context, Name, nameof(SearchCondition)));
            context.CheckFieldFunctionExistence(Name, nameof(OnCellClick), OnCellClick,
                context.GetScriptMethodAttribute(GetType(), nameof(OnCellClick))).AddTo(result);
            if (string.IsNullOrEmpty(SearchCondition.ModuleName)) return result;

            foreach (var variable in Setting.Rows.Concat(Setting.Columns).Select(g => g.Variable)
                .Concat(Setting.Measures.Where(m => m.Function != AggregateFunction.Count).Select(m => m.Variable)))
                context.CheckFieldRelativeVariableExistence(Name, nameof(Setting), SearchCondition.ModuleName, variable).AddTo(result);
            foreach (var (code, message) in ValidateSetting(Setting, context.DesignData, includeUnknownField: false))
                result.Add(Info(context, code, message));
            return result;
        }

        FieldDesignCheckInfo Info(DesignCheckContext context, int code, string message) => new()
        {
            Code = DesignCheckCode.Create(typeof(CrossTabFieldDesign), code),
            Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Setting) },
            Message = message,
        };

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context)).AddMatchCondition(SearchCondition);
            var module = SearchCondition.ModuleName;
            foreach (var group in Setting.Rows.Concat(Setting.Columns))
            {
                var g = group;
                builder.AddVariable(module, g.Variable, s => g.Variable = s);
            }
            foreach (var measure in Setting.Measures.Where(m => !string.IsNullOrEmpty(m.Variable)))
            {
                var m = measure;
                builder.AddVariable(module, m.Variable, s => m.Variable = s);
            }
            return builder.Build();
        }
    }
}
