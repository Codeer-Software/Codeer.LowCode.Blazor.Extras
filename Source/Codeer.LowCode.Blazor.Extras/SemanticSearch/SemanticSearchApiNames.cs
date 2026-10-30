namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>意味検索の再索引が共通の API (ExtrasTransport / Extras.Server の SemanticSearchApi) で使う機能名と操作名。</summary>
    public static class SemanticSearchApiNames
    {
        public const string Feature = "SemanticSearch";

        /// <summary>再索引のジョブを起動する。要求 SemanticSearchReindexRequest、応答 SemanticSearchReindexResponse。</summary>
        public const string Reindex = "Reindex";

        /// <summary>再索引のジョブの状態。要求 SemanticSearchJobRequest、応答 SemanticSearchReindexStatusResponse (知らない Id・他のユーザーのジョブは応答なし)。</summary>
        public const string Status = "Status";

        /// <summary>再索引のジョブの中断。要求 SemanticSearchJobRequest、応答なし。</summary>
        public const string Cancel = "Cancel";
    }
}
