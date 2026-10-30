namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>再索引のジョブの状態の問い合わせと中断 (共通の API の SemanticSearchApiNames.Status / Cancel)。</summary>
    public class SemanticSearchJobRequest
    {
        /// <summary>SemanticSearchReindexResponse.RequestId で受け取ったジョブの Id。</summary>
        public string RequestId { get; set; } = string.Empty;
    }
}
