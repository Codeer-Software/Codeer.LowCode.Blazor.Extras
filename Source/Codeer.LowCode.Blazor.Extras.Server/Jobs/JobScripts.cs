using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Jobs;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// デザインの Job スクリプト (Jobs/名前.job.cs) を、ここのジョブの約束 (戻り値 = 成功の文言・失敗は例外) で動かす。
    /// ホストは、自分のジョブに無い名前をここへ渡すだけでよい (<see cref="JobMode.RunAsync"/> に渡す口・<see cref="JobApi"/> のサーバー内実行の口のどちらからでも)。
    /// スクリプトの Logger は実行先に合わせて出す: ジョブモード (別プロセス) では標準出力、サーバーの中では <see cref="JobProcess.LoggerFactory"/>。
    /// </summary>
    public static class JobScripts
    {
        /// <summary>
        /// デザインにある Job スクリプトの名前。デザインを読めないとき (まだ配置されていない・差し替え中) は空を返す (ジョブの一覧を出すところでは落とさない)。
        /// </summary>
        public static IReadOnlyList<string> GetNames(Func<DesignData> getDesignData)
        {
            try
            {
                return JobScriptRunner.GetJobNames(getDesignData());
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Job スクリプトを 1 つ動かす。読み書きは渡した ModuleDataIO を通る (権限はその ModuleDataIO のユーザー。実行ごとに作ったものを渡す)。
        /// </summary>
        /// <param name="jobName">Job スクリプトの名前 (大文字小文字は区別しない)。</param>
        /// <param name="args">ジョブの引数 (ジョブ名の後ろ)。</param>
        /// <returns>成功したときの文言 (スクリプトの Run の戻り値)。失敗 (デザインに無い名前・スクリプトのエラー・Fail) は例外。</returns>
        public static async Task<string?> RunAsync(ModuleDataIO moduleDataIO, DesignData designData, string jobName, string[] args)
        {
            var name = JobScriptRunner.GetJobNames(designData).FirstOrDefault(e => string.Equals(e, jobName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"ジョブ '{jobName}' がありません");
            var result = await JobScriptRunner.RunAsync(moduleDataIO, designData, name, JobArguments.Parse(args), new ScriptLogger());
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
            return result.Message;
        }

        class ScriptLogger : RequestInterfaces.ILogger
        {
            //ジョブモードの標準出力は親 (JobProcess) がログとして受ける。サーバーの中ではアプリのログへ
            readonly ILogger? _logger = JobMode.IsRunning ? null : JobProcess.LoggerFactory?.CreateLogger(typeof(JobScripts).FullName!);

            public Task Log(string message) => Write(LogLevel.Information, message, message);
            public Task Warn(string message) => Write(LogLevel.Warning, message, $"warn: {message}");
            public Task Error(string message) => Write(LogLevel.Error, message, $"error: {message}");

            Task Write(LogLevel level, string message, string consoleLine)
            {
                if (JobMode.IsRunning) Console.WriteLine(consoleLine);
                else _logger?.Log(level, "{Message}", message);
                return Task.CompletedTask;
            }
        }
    }
}
