using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブを動かす仕組み (ジョブモード)。2 つの側を持つ。
    /// <para>
    /// 子側 (<see cref="RunFromCommandLineAsync(string[], IEnumerable{string}, Func{string, string[], Task{JobRuntimeResult}})"/>): ホストの Program.cs が <c>dotnet &lt;ホスト&gt;.dll job &lt;ジョブ名&gt; [引数...]</c> のときに、
    /// Web ホストを立てずに <c>job</c> より後ろの引数を渡して呼ぶ。ジョブを 1 つ実行してプロセスの終了コードを返す。
    /// 引数の <c>--stdin</c> は標準入力の内容 (UTF-8) に置き換える。
    /// 標準出力・標準エラーは UTF-8。成功したときは最後にジョブの戻り値を 1 行 (null なら空行) 標準出力に書く (画面に出る文言)。
    /// 返すデータ (<see cref="JobRuntimeResult.ResultJson"/>) があるときは、その直前に <see cref="ResultJsonPrefix"/> + JSON の 1 行を書く。
    /// 失敗したときは標準エラーに例外のメッセージだけ (画面に出る文言)、例外の全文は標準出力に出す。
    /// 手で動かすときは <c>echo {"data":{},"submit":null} | dotnet &lt;ホスト&gt;.dll job Sample --stdin</c>。
    /// </para>
    /// <para>
    /// 親側 (<see cref="RunInNewProcessAsync"/>): ジョブモードを持つホスト (このプロセス自身) を <c>job &lt;ジョブ名&gt; --stdin</c> で別プロセスとして起動する。
    /// 入力はコマンドラインの長さの上限に掛からないよう標準入力 (UTF-8) で渡す (<see cref="StandardInputArgument"/>)。
    /// 押したユーザーの Id は環境変数 <see cref="UserIdEnvironmentVariable"/> で渡す (子では設定の <c>Jobs:UserId</c> として読める)。
    /// 標準出力・標準エラーは UTF-8 で読む。成功なら <see cref="JobRuntimeResult"/> を返し、失敗は例外。
    /// </para>
    /// </summary>
    public static class JobRuntime
    {
        /// <summary>終了コード: 成功。</summary>
        public const int ExitCodeSuccess = 0;
        /// <summary>終了コード: 失敗 (ジョブが例外を投げた)。</summary>
        public const int ExitCodeFailure = 1;
        /// <summary>終了コード: ジョブ名のジョブが無い (引数なしで使い方を出したときも)。</summary>
        public const int ExitCodeJobNotFound = 2;

        /// <summary>引数のこの値を標準入力の内容に置き換える。</summary>
        public const string StandardInputArgument = "--stdin";

        /// <summary>成功して返すデータがあるとき、標準出力の最後から 2 行目に書く行の先頭 (この後ろが 1 行の JSON)。</summary>
        public const string ResultJsonPrefix = "#job-result:";

        /// <summary>ジョブを起動したユーザーの Id を子に渡す環境変数 (設定の <c>Jobs:UserId</c> を上書きする)。</summary>
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

        /// <summary>
        /// ジョブを 1 つ実行する (ジョブが返すのは成功の文言だけ)。
        /// </summary>
        /// <param name="args"><c>job</c> より後ろの引数 (ジョブ名・ジョブの引数)。</param>
        /// <param name="jobNames">ホストが持つジョブの名前 (大文字小文字は区別しない)。</param>
        /// <param name="runJob">ジョブを動かす (ジョブ名・ジョブの引数 → 成功の文言。失敗は例外)。ホストがここで DB アクセスとジョブを組み立てる。</param>
        /// <returns>プロセスの終了コード。</returns>
        public static Task<int> RunFromCommandLineAsync(string[] args, IEnumerable<string> jobNames, Func<string, string[], Task<string?>> runJob)
            => RunFromCommandLineAsync(args, jobNames, async (name, jobArgs) => new JobRuntimeResult { Message = await runJob(name, jobArgs) });

        /// <summary>
        /// ジョブを 1 つ実行する。
        /// </summary>
        /// <param name="args"><c>job</c> より後ろの引数 (ジョブ名・ジョブの引数)。</param>
        /// <param name="jobNames">ホストが持つジョブの名前 (大文字小文字は区別しない)。</param>
        /// <param name="runJob">ジョブを動かす (ジョブ名・ジョブの引数 → 成功の文言と返すデータ。失敗は例外)。ホストがここで DB アクセスとジョブを組み立てる。</param>
        /// <returns>プロセスの終了コード。</returns>
        public static async Task<int> RunFromCommandLineAsync(string[] args, IEnumerable<string> jobNames, Func<string, string[], Task<JobRuntimeResult>> runJob)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var names = jobNames.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
            if (args.Length == 0)
            {
                Console.WriteLine($"使い方: {HostName} job <ジョブ名> [引数...]");
                Console.WriteLine($"ジョブ: {string.Join(", ", names)}");
                return ExitCodeJobNotFound;
            }

            var name = names.FirstOrDefault(e => string.Equals(e, args[0], StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                Console.Error.WriteLine($"ジョブ '{args[0]}' がありません");
                Console.WriteLine($"ジョブ: {string.Join(", ", names)}");
                return ExitCodeJobNotFound;
            }

            if (args.Contains(StandardInputArgument) && !Console.IsInputRedirected)
            {
                //端末から --stdin を打つと標準入力の終わりを待ち続ける。間違いだと分かるようにする
                Console.Error.WriteLine($"{StandardInputArgument} には標準入力が要ります (例: echo {{}} | {HostName} job {name} {StandardInputArgument})");
                return ExitCodeFailure;
            }

            JobRuntimeResult? result;
            try
            {
                result = await runJob(name, ReadStandardInput(args[1..]));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.WriteLine(e);
                return ExitCodeFailure;
            }

            //ジョブの出力の後に、行を改めてから書く (ジョブが改行で終わらない出力を残していても混ざらない)。標準出力の最後の行が画面の成功の通知になる (null なら空行 = 既定の文言)
            //返すデータはその直前の 1 行 (JSON の文字列の中に改行は無いので、改行を空白にしても内容は変わらない)
            Console.WriteLine();
            if (!string.IsNullOrEmpty(result?.ResultJson)) Console.WriteLine(ResultJsonPrefix + result.ResultJson.ReplaceLineEndings(" "));
            Console.WriteLine(result?.Message?.ReplaceLineEndings(" ") ?? string.Empty);
            return ExitCodeSuccess;
        }

        /// <summary>
        /// ジョブを別プロセスで動かして終わるまで待つ。戻り値は成功の文言 (ジョブの戻り値) と返したデータ、失敗は例外 (メッセージ = 画面に出す文言)。
        /// <see cref="JobTriggerApi"/> に渡す実行の口の形 (ホストが「このジョブは別プロセスで」と決めたときに呼ぶ)。
        /// 実行中として数えない・失敗を記録しない (どちらも実行の口を呼ぶ <see cref="JobTriggerApi"/> が <see cref="JobTriggerTracker"/> で行う)。
        /// </summary>
        /// <param name="jobName">ジョブの名前。</param>
        /// <param name="input">ジョブに渡す内容 (標準入力。ジョブの引数の <c>--stdin</c> がこれになる)。</param>
        /// <param name="userId">ジョブを起動したユーザーの Id。</param>
        public static async Task<JobRuntimeResult> RunInNewProcessAsync(string jobName, string input, string userId)
        {
            var result = await Launch(jobName, input, userId);
            if (!result.Succeeded) throw new JobRuntimeProcessFailedException(result);
            return new JobRuntimeResult { Message = result.Message, ResultJson = result.ResultJson };
        }

        //別プロセスを起動する。起動と入力の受け渡しはこの呼び出しの中で済ませ (起動の失敗は呼び出し元へ例外)、終了を待つ Task を返す
        internal static Task<JobRuntimeProcessResult> Launch(string jobName, string input, string userId)
        {
            var info = CreateStartInfo(jobName, userId);
            Process? process;
            try
            {
                process = Process.Start(info);
            }
            //OS の文言は実行ファイルのパスを含む。例外のメッセージは画面に出るので、パスの無い文言で包む (原文は内側の例外 = 記録に残る)
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"The job '{jobName}' could not be started.", e);
            }
            if (process == null) throw new InvalidOperationException($"The job '{jobName}' could not be started.");
            return WaitForExitAsync(jobName, process, input);
        }

        static string HostName
            => Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()?.Location ?? Environment.ProcessPath ?? string.Empty);

        //--stdin を標準入力の内容に置き換える (UTF-8。親側が書いて閉じる)
        static string[] ReadStandardInput(string[] args)
        {
            if (!args.Contains(StandardInputArgument)) return args;
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var input = reader.ReadToEnd();
            return args.Select(e => e == StandardInputArgument ? input : e).ToArray();
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
                //子側は標準入力を UTF-8 で読み、標準出力・標準エラーを UTF-8 で書く
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!useHost) info.ArgumentList.Add(EntryDll);
            info.ArgumentList.Add("job");
            info.ArgumentList.Add(jobName);
            info.ArgumentList.Add(StandardInputArgument);
            info.Environment[UserIdEnvironmentVariable] = userId;
            return info;
        }

        //標準出力・標準エラーは読み切る (パイプが詰まって子が止まらないように)
        static async Task<JobRuntimeProcessResult> WaitForExitAsync(string jobName, Process process, string input)
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
                return new JobRuntimeProcessResult(jobName, process.ExitCode, await output, await error);
            }
        }
    }

    //別プロセスで動かしたジョブの失敗 (JobRuntime.RunInNewProcessAsync が投げる)。メッセージは画面に出す文言で、全文は Result が持つ
    sealed class JobRuntimeProcessFailedException(JobRuntimeProcessResult result) : Exception(result.ErrorMessage)
    {
        public JobRuntimeProcessResult Result { get; } = result;
    }
}
