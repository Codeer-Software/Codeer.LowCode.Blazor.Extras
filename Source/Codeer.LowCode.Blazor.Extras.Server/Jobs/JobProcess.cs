using Codeer.LowCode.Blazor.Extras.Jobs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブ (バッチ) モードの親側。ジョブモードを持つホスト (このプロセス自身) を <c>job &lt;ジョブ名&gt; --stdin</c> で別プロセスとして起動する
    /// (終わるまで待つ <see cref="RunAsync"/> / 待たない <see cref="StartAsync"/>)。子側は <see cref="JobMode"/>。
    /// 実行中のジョブはジョブ名 (大文字小文字を区別しない) で数えていて、allowConcurrent が false なら同じ名前が実行中のときは起動しない (このプロセスの中だけの判定)。
    /// <para>
    /// 待たずに起動したジョブは <see cref="StartAsync"/> が返す Id で状態を問い合わせられる (<see cref="GetStatusAsync"/>)。
    /// 状態は <see cref="Cache"/> (IDistributedCache) に置き、終了から <see cref="FinishedRetention"/> を過ぎると消える。
    /// テンプレートはメモリキャッシュなのでサーバーを再起動すると消え、複数インスタンスでは Redis 等の共有キャッシュに差し替える (ロックアウト・ワンタイムコードと同じ)。
    /// サーバーが途中で落ちたジョブは running のまま残る。同時実行の判定はこのプロセスの中だけ。
    /// </para>
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

        /// <summary>
        /// 待たずに起動したジョブの状態の置き場。ホストが起動時に設定する (テンプレートは DI の IDistributedCache)。
        /// 未設定なら <see cref="StartAsync"/> は Id を返すが状態を書かず、<see cref="GetStatusAsync"/> は null を返す。
        /// </summary>
        public static IDistributedCache? Cache { get; set; }

        /// <summary>待たずに起動したジョブの結果を持っておく時間 (終了からの経過。既定 30 分)。過ぎたら <see cref="GetStatusAsync"/> は null を返す。</summary>
        public static TimeSpan FinishedRetention { get; set; } = TimeSpan.FromMinutes(30);

        const string CacheKeyPrefix = "Job:";

        //実行中のジョブの数 (ジョブ名 → 数。同期・非同期とも)
        static readonly Dictionary<string, int> _running = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ジョブを起動して終わるまで待つ。起動できなければ例外。
        /// 戻り値が null = 同時実行を許さず (<paramref name="allowConcurrent"/> が false)、同じ名前のジョブが実行中なので起動しなかった。
        /// </summary>
        /// <param name="jobName">ジョブの名前。</param>
        /// <param name="input">ジョブに渡す内容 (標準入力。ジョブの引数の <c>--stdin</c> がこれになる)。</param>
        /// <param name="userId">ジョブを実行するユーザーの Id。</param>
        /// <param name="allowConcurrent">同じ名前のジョブが実行中でも起動する。</param>
        public static async Task<JobResult?> RunAsync(string jobName, string input, string userId, bool allowConcurrent = true)
        {
            if (!TryEnter(jobName, allowConcurrent)) return null;
            try
            {
                var result = await Launch(jobName, input, CreateStartInfo(jobName, userId));
                if (!result.Succeeded) LogFailure(result.ToString());
                return result;
            }
            finally
            {
                Exit(jobName);
            }
        }

        /// <summary>
        /// ジョブを起動したら待たずに返す (起動できなければ例外)。戻り値はジョブの Id で、<see cref="GetStatusAsync"/> で状態を問い合わせられる。
        /// 戻り値が null = 同時実行を許さず (<paramref name="allowConcurrent"/> が false)、同じ名前のジョブが実行中なので起動しなかった。
        /// 状態は <see cref="Cache"/> に書く (未設定なら書かない。書き込みの失敗は <see cref="LoggerFactory"/> に記録して Id を返す = 状態は分からないがジョブは動いている)。終了は裏で待ち、失敗なら <see cref="LoggerFactory"/> にも記録する。
        /// </summary>
        /// <param name="jobName">ジョブの名前。</param>
        /// <param name="input">ジョブに渡す内容 (標準入力。ジョブの引数の <c>--stdin</c> がこれになる)。</param>
        /// <param name="userId">ジョブを実行するユーザーの Id (<see cref="GetStatusAsync"/> で同じ Id のユーザーだけが状態を見られる)。</param>
        /// <param name="allowConcurrent">同じ名前のジョブが実行中でも起動する。</param>
        public static async Task<string?> StartAsync(string jobName, string input, string userId, bool allowConcurrent = true)
        {
            if (!TryEnter(jobName, allowConcurrent)) return null;
            Task<JobResult> completion;
            try
            {
                completion = Launch(jobName, input, CreateStartInfo(jobName, userId));
            }
            catch
            {
                Exit(jobName);
                throw;
            }
            var cache = Cache;
            var jobId = Guid.NewGuid().ToString("N");
            var entry = new Entry { UserId = userId ?? string.Empty, Status = JobStatus.Running };
            //running を書いてから終了を待ち始める (終了の書き込みを running で上書きしない)。ジョブは起動済みなので、書けなくても Id は返す
            try
            {
                if (cache != null) await cache.SetStringAsync(CacheKeyPrefix + jobId, JsonSerializer.Serialize(entry));
            }
            catch (Exception e)
            {
                LogFailure($"ジョブ {jobName} の状態を書けませんでした: {e.Message}");
            }
            _ = WatchAsync(jobName, cache, jobId, entry, completion);
            return jobId;
        }

        /// <summary>
        /// <see cref="StartAsync"/> で起動したジョブの状態。<see cref="Cache"/> が無い・知らない Id (結果が消えた)・別のユーザーが起動したジョブなら null。
        /// </summary>
        /// <param name="jobId">ジョブの Id (<see cref="StartAsync"/> の戻り値)。</param>
        /// <param name="userId">問い合わせたユーザーの Id。</param>
        public static async Task<JobStatusResponse?> GetStatusAsync(string jobId, string userId)
        {
            var cache = Cache;
            if (cache == null || string.IsNullOrEmpty(jobId)) return null;
            var json = await cache.GetStringAsync(CacheKeyPrefix + jobId);
            if (string.IsNullOrEmpty(json)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(json);
            if (entry == null || entry.UserId != (userId ?? string.Empty)) return null;
            return new JobStatusResponse { Status = entry.Status, Message = entry.Message, Error = entry.Error };
        }

        //実行中として数える。同時実行を許さず同じ名前が実行中なら数えずに false (確認と数えるのを 1 つの lock で)
        static bool TryEnter(string jobName, bool allowConcurrent)
        {
            lock (_running)
            {
                _running.TryGetValue(jobName, out var count);
                if (!allowConcurrent && count > 0) return false;
                _running[jobName] = count + 1;
                return true;
            }
        }

        static void Exit(string jobName)
        {
            lock (_running)
            {
                if (!_running.TryGetValue(jobName, out var count)) return;
                if (count <= 1) _running.Remove(jobName);
                else _running[jobName] = count - 1;
            }
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

        static async Task WatchAsync(string jobName, IDistributedCache? cache, string jobId, Entry entry, Task<JobResult> completion)
        {
            try
            {
                JobResult result;
                try
                {
                    result = await completion;
                }
                finally
                {
                    Exit(jobName);
                }
                if (result.Succeeded)
                {
                    entry.Status = JobStatus.Done;
                    entry.Message = result.Message;
                }
                else
                {
                    entry.Status = JobStatus.Error;
                    entry.Error = result.ErrorMessage;
                    LogFailure(result.ToString());
                }
            }
            catch (Exception e)
            {
                entry.Status = JobStatus.Error;
                entry.Message = string.Empty;
                entry.Error = e.Message;
                LogFailure($"ジョブ {jobName} が失敗しました: {e.Message}");
            }
            if (cache == null) return;
            try
            {
                await cache.SetStringAsync(CacheKeyPrefix + jobId, JsonSerializer.Serialize(entry),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = FinishedRetention });
            }
            catch (Exception e)
            {
                LogFailure($"ジョブ {jobName} の状態を書けませんでした: {e.Message}");
            }
        }

        //記録先が無い・閉じられている (停止中) ときは黙る (裏で待つ StartAsync の Task から例外を漏らさない)
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

        //待たずに起動したジョブ 1 つの状態 (キャッシュに置く JSON)
        sealed class Entry
        {
            public string UserId { get; set; } = string.Empty;
            public JobStatus Status { get; set; }
            public string Message { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }
    }
}
