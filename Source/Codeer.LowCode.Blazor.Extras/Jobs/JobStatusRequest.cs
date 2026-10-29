namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>非同期で起動したジョブの状態の問い合わせ (共通の API の JobApiNames.Status)。</summary>
    public class JobStatusRequest
    {
        /// <summary>JobRunResponse.JobId で受け取ったジョブの Id。</summary>
        public string JobId { get; set; } = string.Empty;
    }
}
