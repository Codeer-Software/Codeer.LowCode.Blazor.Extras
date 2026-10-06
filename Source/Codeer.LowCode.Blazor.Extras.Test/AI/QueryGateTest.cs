using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>QueryGate: データソースごとの同時実行数の上限。</summary>
    public class QueryGateTest
    {
        [Test]
        public async Task 上限0ならゲートなし()
        {
            using var a = await QueryGate.EnterAsync("gate-none", 0, 1, CancellationToken.None);
            using var b = await QueryGate.EnterAsync("gate-none", 0, 1, CancellationToken.None);
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
        }

        [Test]
        public async Task 上限まで入れて超えたら待ち切れでnull_解放すれば入れる()
        {
            const string ds = "gate-limit";
            var a = await QueryGate.EnterAsync(ds, 2, 1, CancellationToken.None);
            var b = await QueryGate.EnterAsync(ds, 2, 1, CancellationToken.None);
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);

            Assert.That(await QueryGate.EnterAsync(ds, 2, 1, CancellationToken.None), Is.Null);

            a!.Dispose();
            a.Dispose();    //二重解放しても数は狂わない
            using var c = await QueryGate.EnterAsync(ds, 2, 1, CancellationToken.None);
            Assert.That(c, Is.Not.Null);
            Assert.That(await QueryGate.EnterAsync(ds, 2, 1, CancellationToken.None), Is.Null, "二重解放で枠が増えていない");
            b!.Dispose();
        }

        [Test]
        public async Task 待っている間に空けば入れる()
        {
            const string ds = "gate-wait";
            var held = await QueryGate.EnterAsync(ds, 1, 1, CancellationToken.None);
            var waiting = QueryGate.EnterAsync(ds, 1, 10, CancellationToken.None);
            await Task.Delay(100);
            Assert.That(waiting.IsCompleted, Is.False);
            held!.Dispose();
            using var entered = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(entered, Is.Not.Null);
        }

        [Test]
        public void データソース名の大小は区別せず上限が違えば別のゲート()
        {
            Assert.That(QueryGate.Get("Gate-Key", 2), Is.SameAs(QueryGate.Get("gate-key", 2)));
            Assert.That(QueryGate.Get("gate-key", 3), Is.Not.SameAs(QueryGate.Get("gate-key", 2)));
        }
    }
}
