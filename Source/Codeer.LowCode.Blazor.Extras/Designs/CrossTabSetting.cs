using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// クロス集計フィールドの集計の設定 (元モジュールと条件は SearchCondition が持つ)。
    /// Rows が行の項目、Columns が列の項目 (どちらも複数可。列が無ければ行だけの表)。Measures が値。
    /// Having (値で絞り込み) と SortConditions (並べ替え) の番号は、Rows + Columns を並べた項目の番号と Measures の番号。
    /// </summary>
    public class CrossTabSetting : ICurrentSettingsText
    {
        public List<AggregateGroup> Rows { get; set; } = new();
        public List<AggregateGroup> Columns { get; set; } = new();
        public List<AggregateMeasure> Measures { get; set; } = new();
        public List<AggregateHaving> Having { get; set; } = new();
        public List<AggregateSort> SortConditions { get; set; } = new();
        /// <summary>表示件数の上限 (集計後のグループ数。未指定なら本体の上限まで)。超えたら表に「表示されていないグループがある」と出る。</summary>
        public int? LimitCount { get; set; }

        public string GetCurrentSettings()
        {
            string Axis(IEnumerable<AggregateGroup> groups) => string.Join(", ", groups.Select(g => FieldOf(g.Variable) + (g is DateGroup d ? $"({d.Bucket})" : string.Empty)));
            var measures = string.Join(", ", Measures.Select(m => m.Function == AggregateFunction.Count ? "Count" : $"{m.Function}({FieldOf(m.Variable)})"));
            return $"{Axis(Rows)} x {Axis(Columns)} : {measures}";
        }

        static string FieldOf(string variable)
            => variable.EndsWith(".Value", StringComparison.Ordinal) ? variable[..^".Value".Length] : variable;
    }

    /// <summary>クロス表の値の見せ方。</summary>
    public enum CrossTabValueDisplay
    {
        /// <summary>集計値そのまま</summary>
        Value,
        /// <summary>総計に対する割合 (%)</summary>
        PercentOfTotal,
        /// <summary>行の合計に対する割合 (%)</summary>
        PercentOfRow,
        /// <summary>列の合計に対する割合 (%)</summary>
        PercentOfColumn,
    }
}
