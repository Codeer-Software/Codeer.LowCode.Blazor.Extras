using Codeer.LowCode.Blazor.SystemSettings;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// キーワード検索 (<see cref="SemanticMatchCondition.Keywords"/>): 文章の列 (DbColumnText) への LIKE の AND。
    /// 語の切り方と LIKE のエスケープを 1 か所で持つ (画面の検索欄・AI の filters・SemanticSearchFieldDesign.CreateWhere が使う)。
    /// </summary>
    internal static class SemanticSearchKeywords
    {
        /// <summary>LIKE のエスケープ文字 (本体の LIKE と同じ)。SQL にはパラメータで渡す。</summary>
        internal const char EscapeChar = (char)1;

        /// <summary>空白 (半角・全角・タブ・改行) で区切った語。前後の空白は除き、同じ語は 1 つにする。</summary>
        internal static List<string> Split(string? keywords)
            => (keywords ?? string.Empty).Split([' ', '　', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).ToList();

        /// <summary>
        /// 語を部分一致の LIKE パターン (%語%) にする。% _ とエスケープ文字は本体と同じくエスケープ文字を前置する。
        /// SQL Server は [ も文字クラスのワイルドカードなのでエスケープする (「[重要]」が任意の 1 文字に化けないように)。
        /// </summary>
        internal static string LikePattern(string term, DataSourceType type)
        {
            var sb = new StringBuilder("%");
            foreach (var c in term)
            {
                if (c == '%' || c == '_' || c == EscapeChar || (type == DataSourceType.SQLServer && c == '[')) sb.Append(EscapeChar);
                sb.Append(c);
            }
            return sb.Append('%').ToString();
        }
    }
}
