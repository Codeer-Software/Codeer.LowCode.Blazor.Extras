namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// 別プロセスで動かしたジョブ 1 回の結果 (<see cref="JobProcess.RunAsync"/> の戻り値)。
    /// 終了コード 0 が成功。成功の文言は標準出力の最後の行 (<see cref="JobMode"/> がジョブの戻り値を書く)、失敗の文言は標準エラーの最初の行 (例外のメッセージ)。
    /// </summary>
    public sealed class JobResult
    {
        public JobResult(string jobName, int exitCode, string output, string error)
        {
            JobName = jobName;
            ExitCode = exitCode;
            Output = output;
            Error = error;
        }

        /// <summary>ジョブの名前。</summary>
        public string JobName { get; }

        /// <summary>プロセスの終了コード (<see cref="JobMode.Success"/> / <see cref="JobMode.Failure"/> / <see cref="JobMode.JobNotFound"/>。起動の失敗はここには来ない = 例外)。</summary>
        public int ExitCode { get; }

        /// <summary>標準出力の全文。</summary>
        public string Output { get; }

        /// <summary>標準エラーの全文。</summary>
        public string Error { get; }

        public bool Succeeded => ExitCode == JobMode.Success;

        /// <summary>成功したときの文言 (ジョブの戻り値 = 標準出力の最後の行)。ジョブが null を返したときは空。</summary>
        public string Message
        {
            get
            {
                var text = Output.EndsWith('\n') ? Output[..^1] : Output;
                return text[(text.LastIndexOf('\n') + 1)..].Trim();
            }
        }

        /// <summary>失敗したときの文言 (標準エラーの最初の空でない行 = 例外のメッセージ)。無ければ「ジョブ X が失敗しました (終了コード N)」。</summary>
        public string ErrorMessage
            => Error.Split('\n').Select(e => e.Trim()).FirstOrDefault(e => e.Length != 0) ?? $"ジョブ {JobName} が失敗しました (終了コード {ExitCode})";

        /// <summary>記録用の文言 (成否・終了コード・標準エラーと標準出力の全文)。</summary>
        public override string ToString()
            => Succeeded
                ? $"ジョブ {JobName} が完了しました: {Output.Trim()}"
                : $"ジョブ {JobName} が失敗しました (終了コード {ExitCode}): {Error.Trim()}{Environment.NewLine}{Output.Trim()}";
    }
}
