namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// 1 回の返事で SQL の実行に使える時間 (<see cref="RawDataAccessOptions.MaxQuerySecondsPerReply"/>)。
    /// 返事の間だけ生きる (置き場は AIChatToolContext.Items)。
    /// </summary>
    internal sealed class QueryTimeBudget
    {
        internal const string ItemKey = "RawDataAccess.QueryTimeBudget";

        readonly object _lock = new();
        readonly TimeSpan? _limit;
        TimeSpan _used;

        /// <param name="limitSeconds">合計の上限 (秒)。0 以下で無制限</param>
        public QueryTimeBudget(int limitSeconds)
            => _limit = limitSeconds > 0 ? TimeSpan.FromSeconds(limitSeconds) : null;

        /// <summary>上限 (秒)。無制限なら 0。</summary>
        public int LimitSeconds => _limit == null ? 0 : (int)_limit.Value.TotalSeconds;

        /// <summary>残り時間。無制限なら null。</summary>
        public TimeSpan? Remaining
        {
            get
            {
                if (_limit == null) return null;
                lock (_lock) return _limit.Value - _used;
            }
        }

        public bool IsExhausted => Remaining is { } r && r <= TimeSpan.Zero;

        public void Add(TimeSpan elapsed)
        {
            lock (_lock) _used += elapsed;
        }

        /// <summary>
        /// 次の SQL のタイムアウト (秒・0 = 無制限)。1 文の上限と残り時間 (切り上げ・最小 1 秒) の小さいほう。
        /// </summary>
        public int CommandTimeoutSeconds(int perCommandSeconds)
        {
            var remaining = Remaining;
            if (remaining == null) return Math.Max(perCommandSeconds, 0);
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling(remaining.Value.TotalSeconds));
            return perCommandSeconds > 0 ? Math.Min(perCommandSeconds, remainingSeconds) : remainingSeconds;
        }

        /// <summary>この返事の予算 (無ければ作って置く)。</summary>
        public static QueryTimeBudget Of(Dictionary<string, object> items, int limitSeconds)
        {
            lock (items)
            {
                if (items.TryGetValue(ItemKey, out var value) && value is QueryTimeBudget budget) return budget;
                budget = new QueryTimeBudget(limitSeconds);
                items[ItemKey] = budget;
                return budget;
            }
        }
    }
}
