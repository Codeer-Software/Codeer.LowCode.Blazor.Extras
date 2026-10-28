namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>非同期で起動したジョブの状態 (<see cref="JobStatusResponse.Status"/>)。</summary>
    public enum JobStatus
    {
        /// <summary>まだ実行中。</summary>
        Running,
        /// <summary>成功して終わった。</summary>
        Done,
        /// <summary>失敗して終わった。</summary>
        Error,
    }
}
