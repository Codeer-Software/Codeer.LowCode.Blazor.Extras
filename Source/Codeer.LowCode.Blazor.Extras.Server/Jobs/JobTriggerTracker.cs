using Codeer.LowCode.Blazor.Extras.JobTrigger;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// 画面 (JobTriggerField) から起動したジョブの実行中の管理 (同時実行の判定・待たずに起動したジョブの状態・失敗の記録)。
    /// <see cref="JobTriggerApi"/> が使う。<see cref="JobTriggerApi"/> を使わずに自分の Controller で受けるホストは <see cref="RunAsync"/> / <see cref="StartAsync(string, string, string, bool)"/> / <see cref="GetStatusAsync"/> を呼ぶ。
    /// 実行中のジョブはジョブ名 (大文字小文字を区別しない) で数えていて、同時実行を許さないジョブは同じ名前が実行中のときは起動しない (このプロセスの中だけの判定)。
    /// <para>
    /// 待たずに起動したジョブの状態は <see cref="Cache"/> (IDistributedCache) に置き、終了から <see cref="FinishedRetention"/> を過ぎると消える。
    /// テンプレートはメモリキャッシュなのでサーバーを再起動すると消え、複数インスタンスでは Redis 等の共有キャッシュに差し替える (ロックアウト・ワンタイムコードと同じ)。
    /// サーバーが途中で落ちたジョブは running のまま残る。同時実行の判定はこのプロセスの中だけ。
    /// </para>
    /// </summary>
    public static class JobTriggerTracker
    {
        /// <summary>失敗したジョブの記録先 (未設定なら記録しない)。同期・非同期とも、失敗したら標準エラーと標準出力の全文 (別プロセス) か例外の全文 (このプロセスの中) を Error で書く。</summary>
        public static ILoggerFactory? LoggerFactory { get; set; }

        /// <summary>
        /// 待たずに起動したジョブの状態の置き場。ホストが起動時に設定する (テンプレートは DI の IDistributedCache)。
        /// 未設定なら待たずに起動したジョブは Id を返すが状態を書かず、状態の問い合わせは応答なしになる。
        /// </summary>
        public static IDistributedCache? Cache { get; set; }

        /// <summary>待たずに起動したジョブの結果を持っておく時間 (終了からの経過。既定 30 分)。過ぎたら状態の問い合わせは応答なしになる。</summary>
        public static TimeSpan FinishedRetention { get; set; } = TimeSpan.FromMinutes(30);

        const string CacheKeyPrefix = "Job:";

        //実行中のジョブの数 (ジョブ名 → 数。同期・非同期とも)
        static readonly Dictionary<string, int> _running = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ジョブを別プロセスで起動して終わるまで待つ (<see cref="JobTriggerApi"/> を使わずに自分の Controller で受けるホスト用)。起動できなければ例外。失敗は <see cref="LoggerFactory"/> に記録する。
        /// 戻り値が null = 同時実行を許さず (<paramref name="allowConcurrent"/> が false)、同じ名前のジョブが実行中なので起動しなかった。
        /// </summary>
        /// <param name="jobName">ジョブの名前。</param>
        /// <param name="input">ジョブに渡す内容 (標準入力。ジョブの引数の <c>--stdin</c> がこれになる)。</param>
        /// <param name="userId">ジョブを起動したユーザーの Id。</param>
        /// <param name="allowConcurrent">同じ名前のジョブが実行中でも起動する。</param>
        public static async Task<JobRuntimeProcessResult?> RunAsync(string jobName, string input, string userId, bool allowConcurrent = true)
        {
            if (!TryEnter(jobName, allowConcurrent)) return null;
            try
            {
                var result = await JobRuntime.Launch(jobName, input, userId);
                if (!result.Succeeded) LogFailure(result.ToString());
                return result;
            }
            finally
            {
                Exit(jobName);
            }
        }

        //JobTriggerApi の実行の口 (ホストが渡す。動く場所はホストが決める) を、実行中として数えて動かす同期版。失敗は記録する。null = 同じ名前が実行中で動かさなかった
        internal static async Task<JobOutcome?> RunTrackedAsync(string jobName, Func<Task<JobRuntimeResult>> run, bool allowConcurrent)
        {
            if (!TryEnter(jobName, allowConcurrent)) return null;
            try
            {
                var outcome = await InvokeDetached(jobName, run);
                if (!outcome.Succeeded) LogFailure(outcome.Log);
                return outcome;
            }
            finally
            {
                Exit(jobName);
            }
        }

        //実行の口の非同期版。要求から切り離して動かし、状態を置き場に書く (GetStatusAsync で問い合わせる)
        internal static Task<string?> StartTrackedAsync(string jobName, Func<Task<JobRuntimeResult>> run, string userId, bool allowConcurrent)
            => StartAsync(jobName, () => InvokeDetached(jobName, run), userId, allowConcurrent);

        //要求の実行コンテキスト (監査ログの AuditContext.Current など AsyncLocal) をジョブに引き継がない (このプロセスの中で動くジョブの書き込みを、起動した要求の記録に混ぜない)。カルチャだけ引き継ぐ
        static Task<JobOutcome> InvokeDetached(string jobName, Func<Task<JobRuntimeResult>> run)
        {
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(() =>
                {
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = uiCulture;
                    return InvokeAsync(jobName, run);
                });
            }
        }

        //ホストの実行の口の約束は JobRuntime の子側と同じ (戻り値 = 成功の文言と返すデータ・失敗は例外)
        static async Task<JobOutcome> InvokeAsync(string jobName, Func<Task<JobRuntimeResult>> run)
        {
            try
            {
                var result = await run();
                return new JobOutcome(true, result?.Message?.ReplaceLineEndings(" ").Trim() ?? string.Empty, string.IsNullOrEmpty(result?.ResultJson) ? null : result.ResultJson, string.Empty, string.Empty);
            }
            //別プロセスの失敗 (JobRuntime.RunInNewProcessAsync)。記録は標準エラーと標準出力の全文
            catch (JobRuntimeProcessFailedException e)
            {
                return new JobOutcome(false, string.Empty, null, e.Result.ErrorMessage, e.Result.ToString());
            }
            catch (Exception e)
            {
                //画面に出す文言は別プロセスと同じく、例外のメッセージの最初の空でない行
                var error = e.Message.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length != 0) ?? $"ジョブ {jobName} が失敗しました";
                return new JobOutcome(false, string.Empty, null, error, $"ジョブ {jobName} が失敗しました: {e}");
            }
        }

        static async Task<JobOutcome> ToOutcomeAsync(Task<JobRuntimeProcessResult> completion)
        {
            var result = await completion;
            return new JobOutcome(result.Succeeded, result.Succeeded ? result.Message : string.Empty, result.ResultJson, result.Succeeded ? string.Empty : result.ErrorMessage, result.ToString());
        }

        /// <summary>
        /// ジョブを別プロセスで起動したら待たずに返す (<see cref="JobTriggerApi"/> を使わずに自分の Controller で受けるホスト用。起動できなければ例外)。戻り値はジョブの Id で、<see cref="GetStatusAsync"/> で状態を問い合わせられる。
        /// 戻り値が null = 同時実行を許さず (<paramref name="allowConcurrent"/> が false)、同じ名前のジョブが実行中なので起動しなかった。
        /// 状態は <see cref="Cache"/> に書く (未設定なら書かない。書き込みの失敗は <see cref="LoggerFactory"/> に記録して Id を返す = 状態は分からないがジョブは動いている)。終了は裏で待ち、失敗なら <see cref="LoggerFactory"/> にも記録する。
        /// </summary>
        /// <param name="jobName">ジョブの名前。</param>
        /// <param name="input">ジョブに渡す内容 (標準入力。ジョブの引数の <c>--stdin</c> がこれになる)。</param>
        /// <param name="userId">ジョブを起動したユーザーの Id (<see cref="GetStatusAsync"/> で同じ Id のユーザーだけが状態を見られる)。</param>
        /// <param name="allowConcurrent">同じ名前のジョブが実行中でも起動する。</param>
        public static Task<string?> StartAsync(string jobName, string input, string userId, bool allowConcurrent = true)
            => StartAsync(jobName, () => ToOutcomeAsync(JobRuntime.Launch(jobName, input, userId)), userId, allowConcurrent);

        //launch は起動まで済ませて終了を待つ Task を返す (起動の失敗はその場で例外)
        static async Task<string?> StartAsync(string jobName, Func<Task<JobOutcome>> launch, string userId, bool allowConcurrent)
        {
            if (!TryEnter(jobName, allowConcurrent)) return null;
            Task<JobOutcome> completion;
            try
            {
                completion = launch();
            }
            catch
            {
                Exit(jobName);
                throw;
            }
            var cache = Cache;
            var jobId = Guid.NewGuid().ToString("N");
            var entry = new Entry { UserId = userId ?? string.Empty, Status = JobTriggerStatus.Running };
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
        /// 待たずに起動したジョブ (<see cref="StartAsync(string, string, string, bool)"/>・<see cref="JobTriggerApi"/> の非同期) の状態。<see cref="Cache"/> が無い・知らない Id (結果が消えた)・別のユーザーが起動したジョブなら null。
        /// </summary>
        /// <param name="jobId">ジョブの Id (<see cref="StartAsync(string, string, string, bool)"/> の戻り値)。</param>
        /// <param name="userId">問い合わせたユーザーの Id。</param>
        public static async Task<JobTriggerStatusResponse?> GetStatusAsync(string jobId, string userId)
        {
            var cache = Cache;
            if (cache == null || string.IsNullOrEmpty(jobId)) return null;
            var json = await cache.GetStringAsync(CacheKeyPrefix + jobId);
            if (string.IsNullOrEmpty(json)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(json);
            if (entry == null || entry.UserId != (userId ?? string.Empty)) return null;
            return new JobTriggerStatusResponse { Status = entry.Status, Message = entry.Message, Error = entry.Error, ResultJson = entry.ResultJson };
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

        static async Task WatchAsync(string jobName, IDistributedCache? cache, string jobId, Entry entry, Task<JobOutcome> completion)
        {
            try
            {
                JobOutcome result;
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
                    entry.Status = JobTriggerStatus.Done;
                    entry.Message = result.Message;
                    entry.ResultJson = result.ResultJson;
                }
                else
                {
                    entry.Status = JobTriggerStatus.Error;
                    entry.Error = result.ErrorMessage;
                    LogFailure(result.Log);
                }
            }
            catch (Exception e)
            {
                entry.Status = JobTriggerStatus.Error;
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
                LoggerFactory?.CreateLogger(typeof(JobTriggerTracker).FullName!).LogError("{Message}", message);
            }
            catch (Exception)
            {
            }
        }

        //ジョブ 1 回の結果 (実行先によらない形)。ResultJson は成功して返したデータがあるときだけ。Log は失敗したときにサーバーのログへ出す全文
        internal sealed record JobOutcome(bool Succeeded, string Message, string? ResultJson, string ErrorMessage, string Log);

        //待たずに起動したジョブ 1 つの状態 (キャッシュに置く JSON)
        sealed class Entry
        {
            public string UserId { get; set; } = string.Empty;
            public JobTriggerStatus Status { get; set; }
            public string Message { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
            //成功したときに返したデータの JSON (無ければ null)
            public string? ResultJson { get; set; }
        }
    }
}
