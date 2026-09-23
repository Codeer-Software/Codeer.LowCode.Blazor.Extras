using Codeer.LowCode.Blazor.Extras.AIChat;

namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// 再索引ジョブの状態 (GET {EndPoint}/{requestId})。<see cref="Status"/> の値は <see cref="AIChatJobStatus"/> と同じ (running / done / error / canceled)。
    /// </summary>
    public class SemanticSearchReindexStatusResponse
    {
        public string Status { get; set; } = AIChatJobStatus.Running;
        /// <summary>書き直した行数 (途中経過・確定値)。</summary>
        public int Processed { get; set; }
        /// <summary>対象の行数 (読める行の数。missingOnly のときはベクトルが無い行の数)。分かるまでは 0。</summary>
        public int Total { get; set; }
        /// <summary>error のときのメッセージ。</summary>
        public string Error { get; set; } = string.Empty;
        public bool IsRunning => Status == AIChatJobStatus.Running;
    }
}
