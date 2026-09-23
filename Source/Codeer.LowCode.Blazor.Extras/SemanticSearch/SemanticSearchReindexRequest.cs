namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField の再索引 (全行の文章とベクトルを作り直す) の開始 (POST {EndPoint})。
    /// サーバーは即時に requestId (<see cref="SemanticSearchReindexResponse"/>) を返し、以降はクライアントが
    /// <see cref="SemanticSearchReindexStatusResponse"/> をポーリングする。
    /// サーバーは ModuleName / FieldName の SemanticSearchField があり、そのフィールドを今のユーザーがユーザー権限だけで読めること
    /// (アプリアクセス条件・モジュールの UserReadCondition・フィールド読取権限) を確かめてから受け付け、行の書き込みは実行ユーザーの権限で行う。
    /// </summary>
    public class SemanticSearchReindexRequest
    {
        /// <summary>SemanticSearchField を置いたモジュール名。</summary>
        public string ModuleName { get; set; } = string.Empty;
        /// <summary>SemanticSearchField のフィールド名。</summary>
        public string FieldName { get; set; } = string.Empty;
        /// <summary>true ならベクトルがまだ無い行だけ (埋め込みに失敗した行の穴埋め)。false なら全行を作り直す (埋め込みモデルを変えたとき等)。</summary>
        public bool MissingOnly { get; set; }
    }
}
