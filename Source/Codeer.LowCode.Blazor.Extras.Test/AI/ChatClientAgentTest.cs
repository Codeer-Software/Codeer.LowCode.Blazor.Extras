using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>ChatClientAgent: 履歴・ストリーミング・ツール呼び出しの往復・グラフのプレースホルダ置換。モデルは台本つきの FakeChatClient。</summary>
    public class ChatClientAgentTest
    {
        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public List<string> Partials { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) => Partials.Add(partialReply.Content);
        }

        static AIChatAgentRequest Request(string message, string conversation = "c1", string user = "alice")
            => new() { ConversationId = conversation, Message = message, UserName = user };

        [Test]
        public async Task Markdownの返事がHTMLになり途中経過として部分的な返事が流れる()
        {
            var client = new FakeChatClient().Text("## 見出し\n\n本文です。");
            var agent = new ChatClientAgent(() => client);
            var progress = new Progress();

            var reply = await agent.ReplyAsync(Request("こんにちは"), progress, CancellationToken.None);

            Assert.That(reply.Format, Is.EqualTo(AIChatReplyFormat.Html));
            Assert.That(reply.Content, Does.Contain("<h2"));
            Assert.That(reply.Content, Does.Contain("本文です。"));
            Assert.That(progress.Partials.Count, Is.GreaterThan(1), "ストリーミングの途中経過");
            Assert.That(progress.Partials.Last(), Does.Contain("本文です。"));

            var sent = client.Calls.Single();
            Assert.That(sent[0].Role, Is.EqualTo(ChatRole.System));
            Assert.That(sent[0].Text, Does.Contain("alice"), "ユーザー名がシステムプロンプトに入る");
            Assert.That(sent.Last().Role, Is.EqualTo(ChatRole.User));
            Assert.That(sent.Last().Text, Is.EqualTo("こんにちは"));
        }

        [Test]
        public async Task 同じ会話では履歴が積まれ別の会話では積まれない()
        {
            var client = new FakeChatClient().Text("A").Text("B").Text("C");
            var agent = new ChatClientAgent(() => client, new ChatClientAgentOptions { StreamPartialReplies = false });
            var progress = new Progress();

            await agent.ReplyAsync(Request("1"), progress, CancellationToken.None);
            await agent.ReplyAsync(Request("2"), progress, CancellationToken.None);
            await agent.ReplyAsync(Request("3", conversation: "other"), progress, CancellationToken.None);

            //2 回目: system + (user1, assistant A) + user2
            var second = client.Calls[1];
            Assert.That(second.Select(m => m.Role.Value), Is.EqualTo(new[] { "system", "user", "assistant", "user" }));
            Assert.That(second[2].Text, Is.EqualTo("A"));
            //別会話: system + user のみ
            Assert.That(client.Calls[2].Count, Is.EqualTo(2));
            Assert.That(agent.ConversationCount, Is.EqualTo(2));
        }

        [Test]
        public async Task 履歴はユーザー発言の境目で古いターンから捨てる()
        {
            var client = new FakeChatClient();
            for (var i = 0; i < 5; i++) client.Text("r" + i);
            var agent = new ChatClientAgent(() => client, new ChatClientAgentOptions { MaxHistoryTurns = 2, StreamPartialReplies = false });
            var progress = new Progress();
            for (var i = 0; i < 5; i++) await agent.ReplyAsync(Request("q" + i), progress, CancellationToken.None);

            //5 回目の送信: system + 直近 2 ターン (q2 r2 q3 r3) + q4
            var last = client.Calls[4];
            Assert.That(last.Select(m => m.Text), Is.EqualTo(new[] { last[0].Text, "q2", "r2", "q3", "r3", "q4" }));
        }

        sealed class EchoToolSet : IAIChatToolSet
        {
            public List<string> Received { get; } = new();
            public string Instructions => "Use echo to repeat text.";
            public IEnumerable<AITool> CreateTools(AIChatToolContext context)
            {
                yield return AIFunctionFactory.Create((string text) =>
                {
                    Received.Add(text);
                    context.Progress.Report("echoing " + text);
                    return "echo:" + text;
                }, "echo", "Repeats the text.");
            }
        }

        [Test]
        public async Task ツール呼び出しが実行されて結果が次の問い合わせに渡り最終の文章が返事になる()
        {
            var client = new FakeChatClient()
                .Call("echo", new Dictionary<string, object?> { ["text"] = "hi" })
                .Text("ツールは echo:hi と返しました。");
            var tools = new EchoToolSet();
            var options = new ChatClientAgentOptions { StreamPartialReplies = false };
            options.ToolSets.Add(tools);
            var agent = new ChatClientAgent(() => client, options);
            var progress = new Progress();

            var reply = await agent.ReplyAsync(Request("echo hi"), progress, CancellationToken.None);

            Assert.That(tools.Received, Is.EqualTo(new[] { "hi" }));
            Assert.That(reply.Content, Does.Contain("echo:hi"));
            Assert.That(progress.Texts, Does.Contain("echoing hi"));
            Assert.That(client.Options[0]!.Tools!.Select(t => t.Name), Is.EqualTo(new[] { "echo" }));
            Assert.That(client.Calls[0][0].Text, Does.Contain("Use echo to repeat text."), "ツールセットの説明がシステムプロンプトに入る");

            //2 回目の問い合わせには呼び出しと結果が入っている
            var second = client.Calls[1];
            Assert.That(second.Any(m => m.Contents.OfType<FunctionCallContent>().Any()), Is.True);
            Assert.That(second.Any(m => m.Contents.OfType<FunctionResultContent>().Any()), Is.True);

            //履歴にも呼び出しと結果が残る (次のターンで送られる)
            client.Text("ok");
            await agent.ReplyAsync(Request("next"), progress, CancellationToken.None);
            Assert.That(client.Calls[2].Any(m => m.Contents.OfType<FunctionResultContent>().Any()), Is.True);
        }

        [Test]
        public async Task グラフのプレースホルダはSVGに置き換わる()
        {
            var client = new FakeChatClient()
                .Call("render_chart", new Dictionary<string, object?>
                {
                    ["chartType"] = "bar",
                    ["title"] = "売上",
                    ["labels"] = new[] { "1月", "2月" },
                    ["series"] = new[] { new { Name = "売上", Values = new double?[] { 10, 20 } } },
                })
                .Text("グラフです。\n\n[[chart:1]]\n\n以上。");
            var options = new ChatClientAgentOptions();
            options.ToolSets.Add(new ChartToolSet());
            var agent = new ChatClientAgent(() => client, options);

            var reply = await agent.ReplyAsync(Request("売上をグラフで"), new Progress(), CancellationToken.None);

            Assert.That(reply.Content, Does.Contain("<div class=\"aichat-chart\"><svg"));
            Assert.That(reply.Content, Does.Not.Contain("[[chart:1]]"));
            Assert.That(reply.Content, Does.Contain("売上"));
            Assert.That(reply.Content, Does.Contain("<rect"));
        }
    }
}
