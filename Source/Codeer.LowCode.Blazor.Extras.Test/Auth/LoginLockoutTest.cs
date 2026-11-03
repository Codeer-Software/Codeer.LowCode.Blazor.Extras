using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>ログインの総当たり対策: 上限でロック、成功で数え直し、ロックの倍増、存在しないアカウント、無効化、同時の試行。</summary>
    public class LoginLockoutTest
    {
        IDistributedCache _cache = null!;

        [SetUp]
        public void SetUp()
            => _cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

        LoginLockout Create(int max = 3) => new(new LoginLockoutSettings { MaxFailedAttempts = max, LockoutMinutes = 5 }, _cache);

        [Test]
        public async Task LocksAfterMaxAttempts()
        {
            var lockout = Create();
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True, "上限に達した回は照合してよい");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            Assert.That(await lockout.TryStartAttemptAsync("hanako"), Is.True, "他のアカウントには効かない");
        }

        [Test]
        public async Task ResetAfterSuccess()
        {
            var lockout = Create();
            await lockout.TryStartAttemptAsync("taro");
            await lockout.TryStartAttemptAsync("taro");
            await lockout.ResetAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False, "成功で 0 から数え直し");
        }

        [Test]
        public async Task SuccessOnTheLastAttemptUnlocks()
        {
            var lockout = Create();
            for (var i = 0; i < 3; i++) await lockout.TryStartAttemptAsync("taro");
            await lockout.ResetAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
        }

        [Test]
        public async Task TwoFactorOnTheLastAttempt()
        {
            //2 回失敗した後: 正しいパスワード (3 回目) でコードを求める → 取り消し → 正しいコードは照合できる
            var lockout = Create();
            await lockout.TryStartAttemptAsync("taro");
            await lockout.TryStartAttemptAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            await lockout.CancelAttemptAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True, "コードを求めた回は失敗に数えない");
            await lockout.ResetAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
        }

        [Test]
        public async Task CodeMismatchCountsAsFailure()
        {
            //パスワードは正しい (取り消し) が、コードの不一致は残る
            var lockout = Create();
            for (var i = 0; i < 3; i++)
            {
                Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
                await lockout.CancelAttemptAsync("taro");
                Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            }
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
        }

        [Test]
        public async Task UnreadableValueStartsOver()
        {
            var lockout = Create();
            await lockout.TryStartAttemptAsync("taro");
            var key = LoginLockout.CacheKey("taro");
            Assert.That(await _cache.GetStringAsync(key), Is.Not.Null);
            await _cache.SetStringAsync(key, "not json");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
        }

        [Test]
        public async Task LockoutLengthDoublesAndResetsOnSuccess()
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var lockout = new LoginLockout(new LoginLockoutSettings { MaxFailedAttempts = 2, LockoutMinutes = 5, MaxLockoutMinutes = 15 }, _cache, () => now);

            async Task<bool> FailTwiceThenTry()
            {
                await lockout.TryStartAttemptAsync("taro");
                await lockout.TryStartAttemptAsync("taro");
                return await lockout.TryStartAttemptAsync("taro");
            }

            //1 回目のロック: 最後の失敗から 5 分
            Assert.That(await FailTwiceThenTry(), Is.False);
            now = now.AddMinutes(4);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            now = now.AddMinutes(1);

            //2 回目: 10 分
            Assert.That(await FailTwiceThenTry(), Is.False);
            now = now.AddMinutes(9);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            now = now.AddMinutes(1);

            //3 回目: 20 分 → 上限 15 分
            Assert.That(await FailTwiceThenTry(), Is.False);
            now = now.AddMinutes(14);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            now = now.AddMinutes(1);

            //サインインできたら最初の 5 分に戻る
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            await lockout.ResetAsync("taro");
            Assert.That(await FailTwiceThenTry(), Is.False);
            now = now.AddMinutes(5);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
        }

        [Test]
        public async Task EmptyKeyIsNotCounted()
        {
            //鍵が無い (空のログイン名) ときは数えない
            var lockout = Create();
            for (var i = 0; i < 20; i++) Assert.That(await lockout.TryStartAttemptAsync(null), Is.True);
            await lockout.CancelAttemptAsync(null);
            await lockout.ResetAsync("");
        }

        [Test]
        public async Task LoginNameIgnoresCaseAndSurroundingSpaces()
        {
            var lockout = Create();
            await lockout.TryStartAttemptAsync("taro");
            await lockout.TryStartAttemptAsync(" TARO ");
            Assert.That(await lockout.TryStartAttemptAsync("Taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False, "同じ名前として数える");
            Assert.That(await lockout.TryStartAttemptAsync("taro2"), Is.True);
            for (var i = 0; i < 20; i++) Assert.That(await lockout.TryStartAttemptAsync("   "), Is.True, "空白だけの名前は数えない");
        }

        [Test]
        public async Task CountsByAccountIdToo()
        {
            //DB の照合順序で同じ行に当たる別表記 (全角半角など) は Id でまとめて数える。どちらかの鍵がロックなら断る
            var lockout = Create();
            await lockout.TryStartAttemptAsync("admin", "U1");
            await lockout.TryStartAttemptAsync("ａｄｍｉｎ", "U1");
            Assert.That(await lockout.TryStartAttemptAsync("ADMIN ", "U1"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("Ａｄｍｉｎ", "U1"), Is.False, "名前の鍵は別でも Id の鍵でロック");
            Assert.That(await lockout.TryStartAttemptAsync("admin", null), Is.True, "名前の鍵は 2 回 (admin / ADMIN) なのでまだ");
            Assert.That(await lockout.TryStartAttemptAsync("admin", null), Is.False, "名前の鍵もロック");
            Assert.That(await lockout.TryStartAttemptAsync("ｘ", "U1"), Is.False);
            Assert.That(await lockout.TryStartAttemptAsync("ｘ", "U2"), Is.True, "Id のロックで断った名前の鍵は数えていない");
            await lockout.ResetAsync("admin", "U1");
            Assert.That(await lockout.TryStartAttemptAsync("Ａｄｍｉｎ", "U1"), Is.True, "サインインで両方数え直す");
        }

        [Test]
        public async Task RecordsExpireSoonerUntilLocked()
        {
            //ロックに至らない記録は 1 日 (MaxLockoutMinutes の 2 倍の方が長ければそれ) で消え、一度ロックされた記録は 30 日残る (存在しない名前を大量に送られても長く残らない)
            var cache = new RecordingCache(_cache);
            var lockout = new LoginLockout(new LoginLockoutSettings { MaxFailedAttempts = 2, LockoutMinutes = 5, MaxLockoutMinutes = 60 }, cache);
            await lockout.TryStartAttemptAsync("taro");
            Assert.That(cache.LastExpiration, Is.EqualTo(TimeSpan.FromDays(1)));
            await lockout.TryStartAttemptAsync("taro");
            Assert.That(cache.LastExpiration, Is.EqualTo(TimeSpan.FromDays(1)));
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            Assert.That(cache.LastExpiration, Is.EqualTo(TimeSpan.FromDays(30)));
        }

        sealed class RecordingCache(IDistributedCache inner) : IDistributedCache
        {
            public TimeSpan? LastExpiration { get; private set; }
            public byte[]? Get(string key) => inner.Get(key);
            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);
            public void Refresh(string key) => inner.Refresh(key);
            public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
            public void Remove(string key) => inner.Remove(key);
            public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
            public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { LastExpiration = options.AbsoluteExpirationRelativeToNow; inner.Set(key, value, options); }
            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            { LastExpiration = options.AbsoluteExpirationRelativeToNow; return inner.SetAsync(key, value, options, token); }
        }

        [Test]
        public async Task RecordOutlivesTheLongestLockout()
        {
            //上限のロックが明けても記録は残り、次のロックも上限の長さ (倍増の段階が最初に戻らない)
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var lockout = new LoginLockout(new LoginLockoutSettings { MaxFailedAttempts = 1, LockoutMinutes = 60, MaxLockoutMinutes = 60 }, _cache, () => now);
            await lockout.TryStartAttemptAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            var key = LoginLockout.CacheKey("taro");
            Assert.That(await _cache.GetStringAsync(key), Is.Not.Null);
            now = now.AddMinutes(60);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            now = now.AddMinutes(59);
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False, "2 回目も上限の 60 分");
        }

        [Test]
        public async Task Disabled()
        {
            var lockout = Create(max: 0);
            for (var i = 0; i < 20; i++) Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.True);
        }

        [Test]
        public async Task ConcurrentAttemptsDoNotExceedTheLimit()
        {
            var lockout = Create(max: 5);
            var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => lockout.TryStartAttemptAsync("taro"))));
            Assert.That(results.Count(r => r), Is.EqualTo(5));
        }
    }
}
