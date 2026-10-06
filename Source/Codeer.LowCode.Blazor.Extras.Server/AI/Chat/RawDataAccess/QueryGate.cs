using System.Collections.Concurrent;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// 同じデータソースへ同時に実行する SQL の本数を絞る (<see cref="RawDataAccessOptions.MaxConcurrentQueries"/>)。
    /// プロセス全体で共有する (対応表が同じ設定の Agent を複数作っても上限は増えない)。
    /// 上限値もキーに入れるので、設定の違う Agent どうしは干渉しない。
    /// </summary>
    internal static class QueryGate
    {
        static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

        /// <summary>
        /// 空くまで待って入る。入れたら解放用の IDisposable、待ち上限を過ぎたら null。上限 0 以下ならゲートなし (すぐ入れる)。
        /// </summary>
        /// <param name="waitSeconds">待ち上限 (秒)。0 以下で空くまで待つ</param>
        public static async Task<IDisposable?> EnterAsync(string dataSourceName, int maxConcurrent, int waitSeconds, CancellationToken cancellationToken)
        {
            if (maxConcurrent <= 0) return Released.Instance;
            var gate = Get(dataSourceName, maxConcurrent);
            var entered = waitSeconds > 0
                ? await gate.WaitAsync(TimeSpan.FromSeconds(waitSeconds), cancellationToken)
                : await gate.WaitAsync(Timeout.Infinite, cancellationToken);
            return entered ? new Releaser(gate) : null;
        }

        internal static SemaphoreSlim Get(string dataSourceName, int maxConcurrent)
            => _gates.GetOrAdd(dataSourceName.ToLowerInvariant() + "|" + maxConcurrent, _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));

        sealed class Releaser : IDisposable
        {
            SemaphoreSlim? _gate;
            public Releaser(SemaphoreSlim gate) => _gate = gate;
            public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
        }

        sealed class Released : IDisposable
        {
            public static readonly Released Instance = new();
            public void Dispose() { }
        }
    }
}
