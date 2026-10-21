using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Extras.Server.Jobs
{
    /// <summary>
    /// ジョブモード (JobRunner) を別プロセスで起動する (終わるまで待つ <see cref="RunAsync"/> / 待たない <see cref="Start"/>)。
    /// 引数の並び (job &lt;ジョブ名&gt; --stdin)・JSON を標準入力で渡すこと・環境変数 (Jobs__UserId)・終了コードは同じフォルダの JobRunner と対。
    /// </summary>
    public static class JobProcess
    {
        /// <summary>dotnet の実行ファイル (既定 "dotnet")。</summary>
        public static string DotnetPath { get; set; } = "dotnet";

        /// <summary>
        /// ジョブモードを持つホストの dll (既定はこのプロセスの入口の dll = Extras.Server.dll)。
        /// 空 (single-file 発行で dll が無い) ならこのプロセスの実行ファイル (Environment.ProcessPath) を直接起動する。
        /// </summary>
        public static string EntryDll { get; set; } = Assembly.GetEntryAssembly()?.Location ?? string.Empty;

        /// <summary>待たずに起動したジョブ (<see cref="Start"/>) が失敗したときの記録先 (未設定なら何もしない)。ホストが Program.cs で ILogger につなぐ。</summary>
        public static Action<string>? Log { get; set; }

        /// <summary>ジョブを起動して終わるまで待つ。戻り値は終了コードと標準出力・標準エラー。</summary>
        public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string jobName, string json, string userId)
            => await Launch(jobName, json, CreateStartInfo(jobName, userId));

        /// <summary>
        /// ジョブを起動したら待たずに返す (起動できなければ例外)。終了は裏で待ち、終了コードが 0 以外なら標準エラーと標準出力を <see cref="Log"/> に流す。
        /// 結果を問い合わせる口は無い。
        /// </summary>
        public static void Start(string jobName, string json, string userId)
        {
            var completion = Launch(jobName, json, CreateStartInfo(jobName, userId));
            _ = WatchAsync(jobName, completion);
        }

        static ProcessStartInfo CreateStartInfo(string jobName, string userId)
        {
            //dll があれば dotnet <dll>、無ければ (single-file 発行) この実行ファイルを直接起動する
            var useDll = !string.IsNullOrEmpty(EntryDll);
            var info = new ProcessStartInfo(useDll ? DotnetPath : Environment.ProcessPath ?? throw new InvalidOperationException("The job host executable could not be found."))
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                //ジョブモードは標準入力を UTF-8 で読み、標準出力・標準エラーを UTF-8 で書く (JobRunner が Console の Encoding を UTF-8 にする)
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (useDll) info.ArgumentList.Add(EntryDll);
            info.ArgumentList.Add("job");
            info.ArgumentList.Add(jobName);
            //JSON はコマンドラインの長さの上限に掛からないよう標準入力で渡す
            info.ArgumentList.Add("--stdin");
            info.Environment["Jobs__UserId"] = userId;
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (!string.IsNullOrEmpty(environment)) info.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
            return info;
        }

        //起動と JSON の受け渡しはこの呼び出しの中で済ませ (起動の失敗は呼び出し元へ例外)、終了を待つ Task を返す
        static Task<(int ExitCode, string Output, string Error)> Launch(string jobName, string json, ProcessStartInfo info)
        {
            var process = Process.Start(info) ?? throw new InvalidOperationException($"The job '{jobName}' could not be started.");
            return WaitForExitAsync(process, json);
        }

        //標準出力・標準エラーは読み切る (パイプが詰まって子が止まらないように)
        static async Task<(int ExitCode, string Output, string Error)> WaitForExitAsync(Process process, string json)
        {
            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.StandardInput.WriteAsync(json);
                }
                catch (IOException)
                {
                    //子が読まずに終わった (ジョブが無い等)。結果は終了コードと標準エラーで返る
                }
                finally
                {
                    try { process.StandardInput.Close(); } catch (IOException) { }
                }
                await process.WaitForExitAsync();
                return (process.ExitCode, await output, await error);
            }
        }

        static async Task WatchAsync(string jobName, Task<(int ExitCode, string Output, string Error)> completion)
        {
            try
            {
                var (exitCode, output, error) = await completion;
                if (exitCode != 0) Log?.Invoke($"ジョブ {jobName} が失敗しました (終了コード {exitCode}): {error.Trim()}{Environment.NewLine}{output.Trim()}");
            }
            catch (Exception e)
            {
                Log?.Invoke($"ジョブ {jobName} が失敗しました: {e.Message}");
            }
        }
    }
}
