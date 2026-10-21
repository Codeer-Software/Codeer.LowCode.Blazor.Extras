namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>ジョブ実行の結果 (待たずに起動したときは起動できたことだけ)。<see cref="ExitCode"/> が 0 なら成功、それ以外は失敗で <see cref="Error"/> にジョブの標準エラーが入る。</summary>
    public class JobRunResponse
    {
        /// <summary>ジョブのプロセスの終了コード。</summary>
        public int ExitCode { get; set; }

        /// <summary>ジョブの標準エラー (前後の空白は除く)。</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>待たずに起動した (<see cref="JobRunRequest.Wait"/> = false) とき true。そのとき <see cref="ExitCode"/> は 0 で、結果は返らない。</summary>
        public bool Started { get; set; }
    }
}
