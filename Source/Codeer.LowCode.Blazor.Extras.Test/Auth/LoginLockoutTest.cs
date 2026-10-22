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
            var key = "codeer.loginlockout.taro";
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
        public async Task NoAccountIsNotCounted()
        {
            //存在しないログイン名 (Id が引けない) は数えない
            var lockout = Create();
            for (var i = 0; i < 20; i++) Assert.That(await lockout.TryStartAttemptAsync(null), Is.True);
            await lockout.CancelAttemptAsync(null);
            await lockout.ResetAsync("");
        }

        [Test]
        public async Task RecordOutlivesTheLongestLockout()
        {
            //上限のロックが明けても記録は残り、次のロックも上限の長さ (倍増の段階が最初に戻らない)
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var lockout = new LoginLockout(new LoginLockoutSettings { MaxFailedAttempts = 1, LockoutMinutes = 60, MaxLockoutMinutes = 60 }, _cache, () => now);
            await lockout.TryStartAttemptAsync("taro");
            Assert.That(await lockout.TryStartAttemptAsync("taro"), Is.False);
            var key = "codeer.loginlockout.taro";
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
