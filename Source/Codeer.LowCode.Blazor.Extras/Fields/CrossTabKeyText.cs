using Codeer.LowCode.Blazor.Extras.Properties;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// クロス表の軸の見出しのうち、年・四半期の文字。年度の開始月が 1 以外なら「2025年度」「2025年度 Q1」(英語は FY2025 / FY2025 Q1)、1 なら「2026」「2026 Q2」。
    /// 鍵は期間の開始日なので、年度は開始日の年、四半期の番号は年度の開始月からの月数で決まる。
    /// </summary>
    internal static class CrossTabKeyText
    {
        public static string Year(int fiscalYear, int fiscalYearStartMonth)
            => fiscalYearStartMonth is <= 1 or > 12 ? fiscalYear.ToString() : string.Format(Resources.CrossTab_FiscalYear, fiscalYear);

        public static string Quarter(int fiscalYear, int quarter, int fiscalYearStartMonth)
            => $"{Year(fiscalYear, fiscalYearStartMonth)} Q{quarter}";

        /// <summary>期間の開始日から年の見出し。</summary>
        public static string Year(DateOnly periodStart, int fiscalYearStartMonth)
            => Year(periodStart.Year, fiscalYearStartMonth);

        /// <summary>期間の開始日から四半期の見出し。年度の開始月より前の月なら前の年度。</summary>
        public static string Quarter(DateOnly periodStart, int fiscalYearStartMonth)
        {
            var start = fiscalYearStartMonth is < 1 or > 12 ? 1 : fiscalYearStartMonth;
            var monthsFromStart = (periodStart.Month - start + 12) % 12;
            var fiscalYear = periodStart.Month >= start ? periodStart.Year : periodStart.Year - 1;
            return Quarter(fiscalYear, monthsFromStart / 3 + 1, fiscalYearStartMonth);
        }
    }
}
