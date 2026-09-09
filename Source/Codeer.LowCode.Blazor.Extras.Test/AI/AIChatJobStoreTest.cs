using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>送信→ポーリング→確定 / 中断 / 所有者違い / 失敗 / Agent 名での振り分け。Agent はテスト用 (FakeAIChatAgent)。</summary>
    public class AIChatJobStoreTest
    {
        static AIChatJobStore CreateStore(TimeSpan? step = null)
            => new(new FakeAIChatAgent { StepDelay = step ?? TimeSpan.FromMilliseconds(20) });

        static async Task<AIChatStatusResponse> WaitDoneAsync(AIChatJobStore store, string owner, string id, int timeoutMs = 10000)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                var s = store.GetStatus(owner, id);
                Assert.That(s, Is.Not.Null);
                if (!s!.IsRunning) return s;
                await Task.Delay(20);
            }
            throw new TimeoutException();
        }

        [Test]
        public async Task 送信すると即requestIdが返りポーリングでHTMLの返事が確定する()
        {
            using var store = CreateStore();
            var id = store.Start("user1", "conv1", "こんにちは");
            Assert.That(id, Is.Not.Empty);

            var first = store.GetStatus("user1", id);
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.Status, Is.EqualTo(AIChatJobStatus.Running));

            var done = await WaitDoneAsync(store, "user1", id);
            Assert.That(done.Status, Is.EqualTo(AIChatJobStatus.Done));
            Assert.That(done.Reply, Does.Contain("<h3"));
            Assert.That(done.Reply, Does.Contain("<table>"));
            Assert.That(done.Reply, Does.Contain("こんにちは"));
            Assert.That(done.Progress, Is.Empty);
        }

        [Test]
        public async Task 途中経過と部分的な返事がポーリングで見える()
        {
            using var store = CreateStore(TimeSpan.FromMilliseconds(120));
            var id = store.Start("u", "c", "経過");
            var sawProgress = false; var sawPartial = false;
            var end = DateTime.Now.AddSeconds(10);
            while (DateTime.Now < end)
            {
                var s = store.GetStatus("u", id)!;
                if (!s.IsRunning) break;
                if (!string.IsNullOrEmpty(s.Progress)) sawProgress = true;
                if (!string.IsNullOrEmpty(s.Reply)) sawPartial = true;
                await Task.Delay(10);
            }
            Assert.That(sawProgress, Is.True, "progress");
            Assert.That(sawPartial, Is.True, "partial reply");
        }

        [Test]
        public async Task HTMLとテキストの返事はそれぞれの形式で正規化される()
        {
            using var store = CreateStore();
            var html = await WaitDoneAsync(store, "u", store.Start("u", "c", "html で"));
            Assert.That(html.Reply, Does.Contain("<div style="));
            Assert.That(html.Reply, Does.Contain("target=\"_blank\""));

            var text = await WaitDoneAsync(store, "u", store.Start("u", "c", "text で"));
            Assert.That(text.Reply, Does.Contain("&lt;b&gt;タグ&lt;/b&gt;"));
            Assert.That(text.Reply, Does.Contain("<br>"));
        }

        [Test]
        public async Task Agentの例外はerrorになる()
        {
            using var store = CreateStore();
            var done = await WaitDoneAsync(store, "u", store.Start("u", "c", "error を起こして"));
            Assert.That(done.Status, Is.EqualTo(AIChatJobStatus.Error));
            Assert.That(done.Error, Does.Contain("意図的な失敗"));
        }

        [Test]
        public async Task 中断するとcanceledになり以後の完了は無視される()
        {
            using var store = CreateStore(TimeSpan.FromSeconds(2));
            var id = store.Start("u", "c", "slow");
            Assert.That(store.Cancel("u", id), Is.True);
            var s = store.GetStatus("u", id)!;
            Assert.That(s.Status, Is.EqualTo(AIChatJobStatus.Canceled));
            await Task.Delay(100);
            Assert.That(store.GetStatus("u", id)!.Status, Is.EqualTo(AIChatJobStatus.Canceled));
        }

        [Test]
        public void 他人のジョブは見えないし中断もできない()
        {
            using var store = CreateStore();
            var id = store.Start("alice", "c", "x");
            Assert.That(store.GetStatus("bob", id), Is.Null);
            Assert.That(store.Cancel("bob", id), Is.False);
            Assert.That(store.GetStatus("alice", "no-such-id"), Is.Null);
        }

        [Test]
        public async Task 終了したジョブは保持期間を過ぎると次の送信で片付く()
        {
            var agent = new FakeAIChatAgent { StepDelay = TimeSpan.FromMilliseconds(10) };
            using var store = new AIChatJobStore(agent, new AIChatJobStoreOptions { FinishedRetention = TimeSpan.Zero });
            var id = store.Start("u", "c", "a");
            await WaitDoneAsync(store, "u", id);
            Assert.That(store.Count, Is.EqualTo(1));
            await Task.Delay(20);
            store.Start("u", "c", "b");
            Assert.That(store.GetStatus("u", id), Is.Null, "古いジョブは消えている");
            Assert.That(store.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task 同じ会話では回数が進む()
        {
            using var store = CreateStore();
            var r1 = await WaitDoneAsync(store, "u", store.Start("u", "conv", "1"));
            var r2 = await WaitDoneAsync(store, "u", store.Start("u", "conv", "2"));
            var other = await WaitDoneAsync(store, "u", store.Start("u", "another", "3"));
            Assert.That(r1.Reply, Does.Contain("(1 回目)"));
            Assert.That(r2.Reply, Does.Contain("(2 回目)"));
            Assert.That(other.Reply, Does.Contain("(1 回目)"));
        }
        [Test]
        public async Task Agent名で登録したAgentに振り分けられ空なら既定になる()
        {
            var a = new FakeAIChatAgent();
            var b = new FakeAIChatAgent();
            var registry = new AIChatAgentRegistry().Add("", a).Add("Raw", b);
            using var store = new AIChatJobStore(registry);

            await WaitDoneAsync(store, "u", store.Start("u", "c", "x"));
            await WaitDoneAsync(store, "u", store.Start("u", "c", "y", "raw"));
            Assert.That(a.Requests.Select(r => r.Message), Is.EqualTo(new[] { "x" }));
            Assert.That(b.Requests.Select(r => r.Message), Is.EqualTo(new[] { "y" }));
            Assert.That(b.Requests[0].AgentName, Is.EqualTo("raw"), "Agent 名は依頼に載る (大文字小文字は登録側で吸収)");
        }

        [Test]
        public async Task 未登録のAgent名はerrorになる()
        {
            var registry = new AIChatAgentRegistry().Add("", new FakeAIChatAgent());
            using var store = new AIChatJobStore(registry);
            var done = await WaitDoneAsync(store, "u", store.Start("u", "c", "x", "NoSuchAgent"));
            Assert.That(done.Status, Is.EqualTo(AIChatJobStatus.Error));
            Assert.That(done.Error, Does.Contain("NoSuchAgent"));
        }

        [Test]
        public void 空文字の登録が無ければ最初に登録したAgentが既定になる()
        {
            var first = new FakeAIChatAgent();
            var registry = new AIChatAgentRegistry().Add("Raw", first).Add("Other", new FakeAIChatAgent());
            Assert.That(registry.Resolve(""), Is.SameAs(first));
            Assert.That(registry.Resolve("RAW"), Is.SameAs(first));
            Assert.That(registry.Resolve("missing"), Is.Null);
        }
    }
}
