using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// ID/パスワードのログインの総当たり対策の設定。appsettings のセクション名はアプリが決める (テンプレートは "LoginLockout")。
    /// </summary>
    public class LoginLockoutSettings
    {
        /// <summary>ログインの失敗 (パスワード・二要素認証のコードの不一致) がこの回数続くとロックする。0 以下で無効。</summary>
        public int MaxFailedAttempts { get; set; } = 10;

        /// <summary>最初のロックの長さ (分。1 以上)。ロックが繰り返されるたびに倍になる。</summary>
        public int LockoutMinutes { get; set; } = 5;

        /// <summary>ロックの長さの上限 (分)。</summary>
        public int MaxLockoutMinutes { get; set; } = 1440;
    }

    /// <summary>
    /// ID/パスワードのログインの総当たり対策。アカウントごとにログインの失敗 (パスワード・二要素認証のコードの不一致) を数え、
    /// MaxFailedAttempts 回続いたら、最後の失敗から一定時間そのアカウントのログインを受け付けない (正しいパスワードでも)。
    /// ロックの長さは LockoutMinutes から始まり、ロックが繰り返されるたびに倍になる (上限 MaxLockoutMinutes)。サインインできたら全部数え直す。
    /// 鍵はアカウントの Id (LoginAccountStore.FindByLoginNameAsync で引く)。存在しないログイン名は数えない (null を渡すと何もしない)。
    /// 照合の前に 1 回分を数えておき、失敗でなければ取り消す。こうすると同時に大量に送られても、1 つのプロセスの中では回数を超えて照合させない
    /// (読んで書くまでの排他はプロセスの中だけ。複数インスタンスで共有キャッシュを使っても、インスタンスをまたいだ同時送信には回数を保証しない)。
    /// 回数の置き場はサーバー側のキャッシュ (IDistributedCache) で DB 列は要らない。記録は最後の試行から 30 日 (MaxLockoutMinutes の 2 倍の方が長ければそれ) で消える。
    /// 呼ぶ順 (TryStartAttempt → 照合 → 不一致は残す / コードを求めた・例外は Cancel / サインインの直前に Reset) は <see cref="PasswordLogin"/> が持つ。
    /// </summary>
    public class LoginLockout
    {
        const string CacheKeyPrefix = "codeer.loginlockout.";

        //記録を残す期間。黙って待てばロックの繰り返しが忘れられる、とならないよう長めに持つ (数えるのは実在するアカウントだけなので件数は増えない)
        static readonly TimeSpan MinRetention = TimeSpan.FromDays(30);

        //読んで書くまでを 1 つずつにする (同じプロセスの中で同じアカウントに同時に来た試行を取りこぼさない)。
        //Id のハッシュで分けるので、別のアカウントどうしはほとんど待たない
        static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

        readonly LoginLockoutSettings _settings;
        readonly IDistributedCache _cache;
        readonly Func<DateTimeOffset> _now;

        public LoginLockout(LoginLockoutSettings settings, IDistributedCache cache)
            : this(settings, cache, () => DateTimeOffset.UtcNow) { }

        internal LoginLockout(LoginLockoutSettings settings, IDistributedCache cache, Func<DateTimeOffset> now)
        {
            _settings = settings;
            _cache = cache;
            _now = now;
        }

        sealed class Entry
        {
            public int Failures { get; set; }
            public DateTimeOffset LastAttemptAt { get; set; }
            public int Lockouts { get; set; }
            public DateTimeOffset LockedUntil { get; set; }
        }

        /// <summary>
        /// ログインの試行を 1 回分数えておく (失敗でなければ CancelAttemptAsync / ResetAsync で外す)。
        /// ロック中なら数えずに false (照合せずに断る)。userId が null / 空 (存在しないログイン名) なら数えずに true。
        /// </summary>
        public async Task<bool> TryStartAttemptAsync(string? userId)
        {
            var allowed = true;
            await UpdateAsync(userId, entry =>
            {
                var now = _now();
                if (entry.LockedUntil > now)
                {
                    allowed = false;
                    return entry;
                }
                if (entry.Failures >= _settings.MaxFailedAttempts)
                {
                    //上限まで失敗した: 最後の失敗からロックする (ロックのたびに長く)
                    entry.Lockouts++;
                    entry.Failures = 0;
                    entry.LockedUntil = entry.LastAttemptAt + LockoutLength(entry.Lockouts);
                    if (entry.LockedUntil > now)
                    {
                        allowed = false;
                        return entry;
                    }
                }
                entry.Failures++;
                entry.LastAttemptAt = now;
                return entry;
            });
            return allowed;
        }

        /// <summary>数えておいた回を取り消す。失敗でもサインインでもなく終わった回 (二要素認証のコードを求めた・メールを送れなかった・照合中の例外) に呼ぶ。</summary>
        public Task CancelAttemptAsync(string? userId)
            => UpdateAsync(userId, entry =>
            {
                if (entry.Failures > 0) entry.Failures--;
                return entry;
            });

        /// <summary>サインインできたので数え直す (ロックの長さも最初に戻す)。</summary>
        public Task ResetAsync(string? userId)
            => UpdateAsync(userId, _ => null);

        TimeSpan LockoutLength(int lockouts)
        {
            var first = Math.Max(1, _settings.LockoutMinutes);
            var minutes = first * Math.Pow(2, Math.Min(lockouts - 1, 30));
            return TimeSpan.FromMinutes(Math.Min(minutes, MaxLockoutMinutes));
        }

        int MaxLockoutMinutes => Math.Max(Math.Max(1, _settings.LockoutMinutes), _settings.MaxLockoutMinutes);

        //update が null を返したら記録を消す
        async Task UpdateAsync(string? userId, Func<Entry, Entry?> update)
        {
            if (_settings.MaxFailedAttempts <= 0 || string.IsNullOrEmpty(userId)) return;
            var key = CacheKeyPrefix + userId;
            var gate = Gates[(key.GetHashCode() & int.MaxValue) % Gates.Length];
            await gate.WaitAsync();
            try
            {
                var entry = update(Read(await _cache.GetStringAsync(key)));
                if (entry == null)
                {
                    await _cache.RemoveAsync(key);
                    return;
                }
                var retention = TimeSpan.FromMinutes(MaxLockoutMinutes * 2.0);
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(entry),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = retention > MinRetention ? retention : MinRetention });
            }
            finally
            {
                gate.Release();
            }
        }

        //読めない値 (別の版が書いた等) は数え始めとして扱う (そのアカウントのログインを例外で止めない)
        static Entry Read(string? json)
        {
            if (json == null) return new Entry();
            try
            {
                return JsonSerializer.Deserialize<Entry>(json) ?? new Entry();
            }
            catch (JsonException)
            {
                return new Entry();
            }
        }
    }
}
