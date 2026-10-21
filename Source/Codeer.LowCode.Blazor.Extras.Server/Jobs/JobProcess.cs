using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ホストのジョブモード (<c>dotnet LowCodeApp.Server.dll job &lt;ジョブ名&gt; [引数...]</c>) を別プロセスで起動する (終わるまで待つ <see cref="RunAsync"/> / 待たない <see cref="Start"/>)。
    /// 引数は JSON 1 つ。実行ユーザーは環境変数 <c>Jobs__UserId</c> (ホストの appsettings の Jobs:UserId を上書き) で渡す。
    /// </summary>
    public static class JobProcess
    {
        /// <summary>dotnet の実行ファイル (既定 "dotnet")。</summary>
        public static string DotnetPath { get; set; } = "dotnet";

        /// <summary>ジョブモードを持つホストの dll (既定はこのプロセスの入口の dll = LowCodeApp.Server.dll)。</summary>
        public static string EntryDll { get; set; } = Assembly.GetEntryAssembly()?.Location ?? string.Empty;

        //テスト用: プロセスを起動する代わりに呼ぶ
        internal static Func<ProcessStartInfo, Task<(int ExitCode, string Output, string Error)>>? StartOverride { get; set; }

        /// <summary>待たずに起動したジョブ (<see cref="Start"/>) が失敗したときの記録先 (未設定なら何もしない)。ホストが Program.cs で ILogger につなぐ。</summary>
        public static Action<string>? Log { get; set; }

        /// <summary>ジョブを起動して終わるまで待つ。戻り値は終了コードと標準出力・標準エラー。</summary>
        public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string jobName, string json, string userId)
            => await Launch(jobName, CreateStartInfo(jobName, json, userId));

        /// <summary>
        /// ジョブを起動したら待たずに返す (起動できなければ例外)。終了は裏で待ち、終了コードが 0 以外なら標準エラーを <see cref="Log"/> に流す。
        /// 結果を問い合わせる口は無い。
        /// </summary>
        public static void Start(string jobName, string json, string userId)
        {
            var completion = Launch(jobName, CreateStartInfo(jobName, json, userId));
            _ = WatchAsync(jobName, completion);
        }

        static ProcessStartInfo CreateStartInfo(string jobName, string json, string userId)
        {
            var info = new ProcessStartInfo(DotnetPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                //ジョブモードは標準出力・標準エラーを UTF-8 で書く (テンプレの JobRunner が Console.OutputEncoding を UTF-8 にする)
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(EntryDll);
            info.ArgumentList.Add("job");
            info.ArgumentList.Add(jobName);
            info.ArgumentList.Add(json);
            info.Environment["Jobs__UserId"] = userId;
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (!string.IsNullOrEmpty(environment)) info.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
            return info;
        }

        //起動はこの呼び出しの中で済ませ (起動の失敗は呼び出し元へ例外)、終了を待つ Task を返す
        static Task<(int ExitCode, string Output, string Error)> Launch(string jobName, ProcessStartInfo info)
        {
            if (StartOverride != null) return StartOverride(info);
            var process = Process.Start(info) ?? throw new InvalidOperationException($"The job '{jobName}' could not be started.");
            return WaitForExitAsync(process);
        }

        //標準出力・標準エラーは読み切る (パイプが詰まって子が止まらないように)
        static async Task<(int ExitCode, string Output, string Error)> WaitForExitAsync(Process process)
        {
            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return (process.ExitCode, await output, await error);
            }
        }

        static async Task WatchAsync(string jobName, Task<(int ExitCode, string Output, string Error)> completion)
        {
            try
            {
                var (exitCode, _, error) = await completion;
                if (exitCode != 0) Log?.Invoke($"ジョブ {jobName} が失敗しました (終了コード {exitCode}): {error.Trim()}");
            }
            catch (Exception e)
            {
                Log?.Invoke($"ジョブ {jobName} が失敗しました: {e.Message}");
            }
        }
    }
}
