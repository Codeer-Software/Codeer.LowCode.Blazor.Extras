namespace Codeer.LowCode.Blazor.Extras.AIChat
{
    /// <summary>返事のジョブの状態の問い合わせと中断 (共通の API の AIChatApiNames.Status / Cancel)。</summary>
    public class AIChatJobRequest
    {
        /// <summary>AIChatSendResponse.RequestId で受け取ったジョブの Id。</summary>
        public string RequestId { get; set; } = string.Empty;
    }
}
