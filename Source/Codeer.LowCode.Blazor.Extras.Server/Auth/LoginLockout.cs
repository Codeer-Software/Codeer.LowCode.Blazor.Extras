using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text;
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
    /// ID/パスワードのログインの総当たり対策。ログインの失敗 (パスワード・二要素認証のコードの不一致) を数え、
    /// MaxFailedAttempts 回続いたら、最後の失敗から一定時間ログインを受け付けない (正しいパスワードでも)。
    /// ロックの長さは LockoutMinutes から始まり、ロックが繰り返されるたびに倍になる (上限 MaxLockoutMinutes)。サインインできたら全部数え直す。
    /// 鍵は 2 つ: 入力されたログイン名 (前後の空白と大文字小文字を区別しない) と、その名前で引けたアカウントの Id。両方で数え、どちらかがロック中なら断る。
    /// 名前で数えるのはアカウントの有無でロックの応答を変えないため (実在するアカウントだけがロックされると、応答で有無が分かる)。
    /// Id で数えるのは DB の照合順序で同じ行に当たる別表記 (全角半角など) をまとめて数えるため。null / 空白だけの名前・null / 空の Id は数えない。
    /// 照合の前に 1 回分を数えておき、失敗でなければ取り消す。こうすると同時に大量に送られても、1 つのプロセスの中では回数を超えて照合させない
    /// (読んで書くまでの排他はプロセスの中だけ。複数インスタンスで共有キャッシュを使っても、インスタンスをまたいだ同時送信には回数を保証しない)。
    /// 回数の置き場はサーバー側のキャッシュ (IDistributedCache) で DB 列は要らない (名前のキーはハッシュ = 長さを固定し名前を残さない)。
    /// 記録は最後の試行から 1 日 (MaxLockoutMinutes の 2 倍の方が長ければそれ) で消え、一度でもロックされた記録は 30 日残す
    /// (黙って待てばロックの繰り返しが忘れられる、とならない。存在しない名前を大量に送られても、ロックに至らない記録は長く残らない)。
    /// 呼ぶ順 (TryStartAttempt → 照合 → 不一致は残す / コードを求めた・例外は Cancel / サインインの直前に Reset) は <see cref="PasswordLogin"/> が持つ。
    /// </summary>
    public class LoginLockout
    {
        const string CacheKeyPrefix = "codeer.loginlockout.";

        //記録を残す期間。ロックに至らない記録は短く (名前の大量送信で積み上がらない)、一度でもロックされた記録は長く (黙って待てばロックの繰り返しが忘れられる、とならない)
        static readonly TimeSpan UnlockedRetention = TimeSpan.FromDays(1);
        static readonly TimeSpan LockedRetention = TimeSpan.FromDays(30);

        //読んで書くまでを 1 つずつにする (同じプロセスの中で同じ鍵に同時に来た試行を取りこぼさない)。
        //鍵のハッシュで分けるので、別のアカウントどうしはほとんど待たない
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

        //ログイン名 → キャッシュのキー (前後の空白と大文字小文字を区別しない。ハッシュで長さを固定する)
        internal static string CacheKey(string loginName)
            => CacheKeyPrefix + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(loginName.Trim().ToUpperInvariant())));

        //アカウントの Id → キャッシュのキー
        internal static string IdKey(string userId) => CacheKeyPrefix + "id:" + userId;

        static IEnumerable<string> Keys(string? loginName, string? userId)
        {
            if (!string.IsNullOrWhiteSpace(loginName)) yield return CacheKey(loginName);
            if (!string.IsNullOrEmpty(userId)) yield return IdKey(userId);
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
        /// どちらかの鍵がロック中なら数えずに false (照合せずに断る)。userId はログイン名で引けたアカウントの Id (無ければ null)。
        /// </summary>
        public async Task<bool> TryStartAttemptAsync(string? loginName, string? userId = null)
        {
            var started = new List<string>();
            try
            {
                foreach (var key in Keys(loginName, userId))
                {
                    if (await TryStartKeyAsync(key))
                    {
                        started.Add(key);
                        continue;
                    }
                    foreach (var e in started) await CancelKeyAsync(e);
                    return false;
                }
                return true;
            }
            catch
            {
                //片方を数えた後の障害: 数えた分を残さない (取り消しの失敗で元の例外を隠さない)
                try { foreach (var e in started) await CancelKeyAsync(e); } catch { }
                throw;
            }
        }

        /// <summary>数えておいた回を取り消す。失敗でもサインインでもなく終わった回 (二要素認証のコードを求めた・メールを送れなかった・照合中の例外) に呼ぶ。</summary>
        public async Task CancelAttemptAsync(string? loginName, string? userId = null)
        {
            foreach (var key in Keys(loginName, userId)) await CancelKeyAsync(key);
        }

        /// <summary>サインインできたので数え直す (ロックの長さも最初に戻す)。</summary>
        public async Task ResetAsync(string? loginName, string? userId = null)
        {
            foreach (var key in Keys(loginName, userId)) await UpdateAsync(key, _ => null);
        }

        async Task<bool> TryStartKeyAsync(string key)
        {
            var allowed = true;
            await UpdateAsync(key, entry =>
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

        Task CancelKeyAsync(string key)
            => UpdateAsync(key, entry =>
            {
                if (entry.Failures > 0) entry.Failures--;
                return entry;
            });

        TimeSpan LockoutLength(int lockouts)
        {
            var first = Math.Max(1, _settings.LockoutMinutes);
            var minutes = first * Math.Pow(2, Math.Min(lockouts - 1, 30));
            return TimeSpan.FromMinutes(Math.Min(minutes, MaxLockoutMinutes));
        }

        int MaxLockoutMinutes => Math.Max(Math.Max(1, _settings.LockoutMinutes), _settings.MaxLockoutMinutes);

        //update が null を返したら記録を消す
        async Task UpdateAsync(string key, Func<Entry, Entry?> update)
        {
            if (_settings.MaxFailedAttempts <= 0) return;
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
                var minimum = entry.Lockouts > 0 ? LockedRetention : UnlockedRetention;
                if (retention < minimum) retention = minimum;
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(entry), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = retention });
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
