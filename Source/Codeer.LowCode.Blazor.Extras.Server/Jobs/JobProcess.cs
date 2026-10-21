using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブ (バッチ) モードの親側。ジョブモードを持つホスト (このプロセス自身) を <c>job &lt;ジョブ名&gt; --stdin</c> で別プロセスとして起動する
    /// (終わるまで待つ <see cref="RunAsync"/> / 待たない <see cref="Start"/>)。子側は <see cref="JobMode"/>。
    /// <para>
    /// 子側との約束: 入力はコマンドラインの長さの上限に掛からないよう標準入力 (UTF-8) で渡す (<see cref="JobMode.StandardInputArgument"/>)。
    /// 押したユーザーの Id は環境変数 <see cref="UserIdEnvironmentVariable"/> で渡す (子では設定の <c>Jobs:UserId</c> として読める)。
    /// 標準出力・標準エラーは UTF-8 で読む。結果は <see cref="JobResult"/>。
    /// </para>
    /// </summary>
    public static class JobProcess
    {
        /// <summary>ジョブを実行するユーザーの Id を子に渡す環境変数 (設定の <c>Jobs:UserId</c> を上書きする)。</summary>
        public const string UserIdEnvironmentVariable = "Jobs__UserId";

        /// <summary>ジョブモードを持つホストの dll (既定はこのプロセスの入口の dll。single-file 発行では空)。</summary>
        public static string EntryDll { get; set; } = Assembly.GetEntryAssembly()?.Location ?? string.Empty;

        /// <summary>
        /// ジョブモードを起動する実行ファイル。既定は <see cref="EntryDll"/> の隣の apphost (Windows は .exe、Linux は拡張子なし)、
        /// それが無ければ (single-file 発行) このプロセスの実行ファイル。apphost はランタイムを自分で見つける (DOTNET_ROOT・既定のインストール先・
        /// self-contained なら自分のフォルダ) ので、IIS (w3wp.exe) の下や PATH に dotnet が無いサーバーでも動く。
        /// 空なら <see cref="DotnetPath"/> で <see cref="EntryDll"/> を起動する (dotnet &lt;dll&gt;)。
        /// </summary>
        public static string HostExecutable { get; set; } = GetDefaultHostExecutable();

        /// <summary><see cref="HostExecutable"/> が空のときに使う dotnet の実行ファイル (既定はこのプロセスを起動した dotnet。dotnet から起動されていなければ PATH の "dotnet")。</summary>
        public static string DotnetPath { get; set; } = GetDefaultDotnetPath();

        /// <summary>失敗したジョブの記録先 (未設定なら記録しない)。同期・非同期とも、終了コードが 0 以外なら標準エラーと標準出力の全文を Error で書く。</summary>
        public static ILoggerFactory? LoggerFactory { get; set; }

        /// <summary>ジョブを起動して終わるまで待つ。起動できなければ例外。</summary>
        /// <param name="jobName">ジョブの名前。</param>
        /// <param name="input">ジョブに渡す内容 (標準入力。ジョブの引数の <c>--stdin</c> がこれになる)。</param>
        /// <param name="userId">ジョブを実行するユーザーの Id。</param>
        public static async Task<JobResult> RunAsync(string jobName, string input, string userId)
        {
            var result = await Launch(jobName, input, CreateStartInfo(jobName, userId));
            if (!result.Succeeded) LogFailure(result.ToString());
            return result;
        }

        /// <summary>
        /// ジョブを起動したら待たずに返す (起動できなければ例外)。終了は裏で待ち、失敗なら <see cref="LoggerFactory"/> に記録する。結果を問い合わせる口は無い。
        /// </summary>
        public static void Start(string jobName, string input, string userId)
        {
            var completion = Launch(jobName, input, CreateStartInfo(jobName, userId));
            _ = WatchAsync(jobName, completion);
        }

        //EntryDll の隣の apphost。single-file 発行 (dll が無い) ならこのプロセスの実行ファイル。どちらも無ければ空 (dotnet <dll> で起動する)
        static string GetDefaultHostExecutable()
        {
            if (string.IsNullOrEmpty(EntryDll)) return Environment.ProcessPath ?? string.Empty;
            var appHost = Path.ChangeExtension(EntryDll, OperatingSystem.IsWindows() ? ".exe" : null);
            if (!File.Exists(appHost)) return string.Empty;
            //Windows から発行して Linux に置いた apphost は実行ビットが無いことがある。そのときは dotnet <dll> に落とす
            if (!OperatingSystem.IsWindows() && !File.GetUnixFileMode(appHost).HasFlag(UnixFileMode.UserExecute)) return string.Empty;
            return appHost;
        }

        static string GetDefaultDotnetPath()
        {
            var fileName = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
            var isDotnet = string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
            return isDotnet ? Environment.ProcessPath! : "dotnet";
        }

        static ProcessStartInfo CreateStartInfo(string jobName, string userId)
        {
            //apphost があればそれを直接、無ければ dotnet <dll>
            var useHost = !string.IsNullOrEmpty(HostExecutable);
            if (!useHost && string.IsNullOrEmpty(EntryDll)) throw new InvalidOperationException("The job host executable could not be found.");
            var info = new ProcessStartInfo(useHost ? HostExecutable : DotnetPath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                //子 (JobMode) は標準入力を UTF-8 で読み、標準出力・標準エラーを UTF-8 で書く
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!useHost) info.ArgumentList.Add(EntryDll);
            info.ArgumentList.Add("job");
            info.ArgumentList.Add(jobName);
            info.ArgumentList.Add(JobMode.StandardInputArgument);
            info.Environment[UserIdEnvironmentVariable] = userId;
            return info;
        }

        //起動と入力の受け渡しはこの呼び出しの中で済ませ (起動の失敗は呼び出し元へ例外)、終了を待つ Task を返す
        static Task<JobResult> Launch(string jobName, string input, ProcessStartInfo info)
        {
            var process = Process.Start(info) ?? throw new InvalidOperationException($"The job '{jobName}' could not be started.");
            return WaitForExitAsync(jobName, process, input);
        }

        //標準出力・標準エラーは読み切る (パイプが詰まって子が止まらないように)
        static async Task<JobResult> WaitForExitAsync(string jobName, Process process, string input)
        {
            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.StandardInput.WriteAsync(input);
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
                return new JobResult(jobName, process.ExitCode, await output, await error);
            }
        }

        static async Task WatchAsync(string jobName, Task<JobResult> completion)
        {
            try
            {
                var result = await completion;
                if (!result.Succeeded) LogFailure(result.ToString());
            }
            catch (Exception e)
            {
                LogFailure($"ジョブ {jobName} が失敗しました: {e.Message}");
            }
        }

        //記録先が無い・閉じられている (停止中) ときは黙る (裏で待つ Start の Task から例外を漏らさない)
        static void LogFailure(string message)
        {
            try
            {
                LoggerFactory?.CreateLogger(typeof(JobProcess).FullName!).LogError("{Message}", message);
            }
            catch (Exception)
            {
            }
        }
    }
}
