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
        /// <summary>ジョブモードを持つホストの dll (既定はこのプロセスの入口の dll = Extras.Server.dll。single-file 発行では空)。</summary>
        public static string EntryDll { get; set; } = Assembly.GetEntryAssembly()?.Location ?? string.Empty;

        /// <summary>
        /// ジョブモードを起動する実行ファイル。既定は EntryDll の隣の apphost (Extras.Server.exe。Linux は拡張子なし)、
        /// それが無ければ (single-file 発行) このプロセスの実行ファイル。apphost はランタイムを自分で見つける (DOTNET_ROOT・既定のインストール先・
        /// self-contained なら自分のフォルダ) ので、IIS (w3wp.exe) の下や PATH に dotnet が無いサーバーでも動く。
        /// 空なら <see cref="DotnetPath"/> で EntryDll を起動する (dotnet &lt;dll&gt;)。
        /// </summary>
        public static string HostExecutable { get; set; } = GetDefaultHostExecutable();

        /// <summary>HostExecutable が空のときに使う dotnet の実行ファイル (既定はこのプロセスを起動した dotnet。dotnet から起動されていなければ PATH の "dotnet")。</summary>
        public static string DotnetPath { get; set; } = GetDefaultDotnetPath();

        /// <summary>ジョブが失敗したときの記録先 (未設定なら何もしない)。ホストが Program.cs で ILogger につなぐ。</summary>
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

        /// <summary>標準出力の最後の行 (JobRunner が書くジョブの戻り値。画面の成功の通知に出す文言)。空行なら空。</summary>
        public static string LastLine(string output)
        {
            var text = output.EndsWith('\n') ? output[..^1] : output;
            return text[(text.LastIndexOf('\n') + 1)..].Trim();
        }

        /// <summary>標準エラーの最初の空でない行 (JobRunner が書く例外のメッセージ。画面のエラーに出す文言)。無ければ空。</summary>
        public static string FirstLine(string error)
            => error.Split('\n').Select(e => e.Trim()).FirstOrDefault(e => e.Length != 0) ?? string.Empty;

        /// <summary>失敗したジョブの記録の文言 (標準エラーと標準出力をまとめる)。</summary>
        public static string FormatFailure(string jobName, int exitCode, string output, string error)
            => $"ジョブ {jobName} が失敗しました (終了コード {exitCode}): {error.Trim()}{Environment.NewLine}{output.Trim()}";

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
                //ジョブモードは標準入力を UTF-8 で読み、標準出力・標準エラーを UTF-8 で書く (Program.cs と JobRunner が Console の Encoding を UTF-8 にする)
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!useHost) info.ArgumentList.Add(EntryDll);
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
                if (exitCode != 0) Log?.Invoke(FormatFailure(jobName, exitCode, output, error));
            }
            catch (Exception e)
            {
                Log?.Invoke($"ジョブ {jobName} が失敗しました: {e.Message}");
            }
        }
    }
}
