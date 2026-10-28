namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>
    /// 非同期で起動したジョブの状態。フィールドの GetJobStatus が返す (HTTP では GET <see cref="Fields.JobTriggerField.EndPoint"/>/{jobId} の応答)。
    /// </summary>
    public class JobStatusResponse
    {
        /// <summary>状態。</summary>
        public JobStatus Status { get; set; }

        /// <summary>Done のときの文言 (ジョブの戻り値。無ければ空)。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Error のときのエラーの文言。</summary>
        public string Error { get; set; } = string.Empty;
    }
}
