using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブのスクリプトの Logger の出し先。
    /// logger を渡すとそこへ (プロセス内実行でアプリのログに出す)、渡さなければ標準出力へ出す (ジョブモード。Warn / Error は行の先頭に warn: / error: を付ける)。
    /// </summary>
    public sealed class JobScriptLogger(ILogger? logger = null) : RequestInterfaces.ILogger
    {
        public Task Log(string message) => Write(LogLevel.Information, message, message);
        public Task Warn(string message) => Write(LogLevel.Warning, message, $"warn: {message}");
        public Task Error(string message) => Write(LogLevel.Error, message, $"error: {message}");

        Task Write(LogLevel level, string message, string consoleLine)
        {
            if (logger == null) Console.WriteLine(consoleLine);
            else logger.Log(level, "{Message}", message);
            return Task.CompletedTask;
        }
    }
}
