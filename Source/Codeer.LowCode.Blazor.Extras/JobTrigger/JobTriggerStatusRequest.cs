namespace Codeer.LowCode.Blazor.Extras.JobTrigger
{
    /// <summary>非同期で起動したジョブの状態の問い合わせ (共通の API の JobTriggerApiNames.Status)。</summary>
    public class JobTriggerStatusRequest
    {
        /// <summary>JobTriggerRunResponse.JobId で受け取ったジョブの Id。</summary>
        public string JobId { get; set; } = string.Empty;
    }
}
