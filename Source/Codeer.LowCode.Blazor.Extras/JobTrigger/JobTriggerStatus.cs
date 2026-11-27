namespace Codeer.LowCode.Blazor.Extras.JobTrigger
{
    /// <summary>非同期で起動したジョブの状態 (<see cref="JobTriggerStatusResponse.Status"/>)。</summary>
    public enum JobTriggerStatus
    {
        /// <summary>まだ実行中。</summary>
        Running,
        /// <summary>成功して終わった。</summary>
        Done,
        /// <summary>失敗して終わった。</summary>
        Error,
    }
}
