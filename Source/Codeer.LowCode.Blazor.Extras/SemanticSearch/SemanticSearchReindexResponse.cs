namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>再索引の開始の応答。以降は requestId で <see cref="SemanticSearchReindexStatusResponse"/> をポーリングする。</summary>
    public class SemanticSearchReindexResponse
    {
        public string RequestId { get; set; } = string.Empty;
    }
}
