namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブが成功したときにホストの実行の口が返すもの (<see cref="JobMode"/> の runJob・<see cref="JobApi"/> の run・<see cref="JobProcess.RunInNewProcessAsync"/>)。
    /// 失敗は例外で返す (この型には失敗を入れない)。
    /// </summary>
    public sealed class JobRunResult
    {
        /// <summary>成功の文言 (画面の成功の通知に出る)。null なら既定の文言。</summary>
        public string? Message { get; init; }

        /// <summary>
        /// 起動した側に返すデータの JSON (Job スクリプトの args.Result = 本体の JobExecutionResult.ResultJson)。無ければ null。
        /// 画面では JobTriggerField の Result (同期) / GetJobStatus の Result (非同期) になる。
        /// </summary>
        public string? ResultJson { get; init; }
    }
}
