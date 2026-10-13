namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// キーワード検索 (<see cref="SemanticMatchCondition.Keywords"/>) の語の切り方 (画面の検索欄・AI の filters・SemanticSearchFieldDesign.CreateWhere が使う)。
    /// 語ごとの LIKE (パターン・エスケープ) は本体の SqlConditionContext.Like。
    /// </summary>
    internal static class SemanticSearchKeywords
    {
        /// <summary>空白 (半角・全角・タブ・改行) で区切った語。前後の空白は除き、同じ語は 1 つにする。</summary>
        internal static List<string> Split(string? keywords)
            => (keywords ?? string.Empty).Split([' ', '　', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal).ToList();
    }
}
