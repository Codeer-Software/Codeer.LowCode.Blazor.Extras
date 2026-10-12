namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>SemanticSearchField を検索レイアウトに置いたときに出す入力欄。</summary>
    public enum SemanticSearchBoxes
    {
        /// <summary>文章の欄だけ (意味検索)</summary>
        Semantic,
        /// <summary>文章の欄とキーワードの欄 (両方入れると、キーワードで絞ったうえで近い順)</summary>
        SemanticAndKeyword,
        /// <summary>キーワードの欄だけ (ベクトル検索に対応しない DB・埋め込み無しの構成。文章の欄は出さない)</summary>
        KeywordOnly,
    }
}
