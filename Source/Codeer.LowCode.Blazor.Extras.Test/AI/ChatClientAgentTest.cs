using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
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
            public string GetInstructions(AIChatToolContext context) => "Use echo to repeat text.";
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
        [Test]
        public async Task 同じ会話IDでもユーザーが違えば別の会話になる()
        {
            var client = new FakeChatClient().Text("A").Text("B").Text("C");
            var agent = new ChatClientAgent(() => client, new ChatClientAgentOptions { StreamPartialReplies = false });
            var progress = new Progress();

            await agent.ReplyAsync(Request("alice の 1 回目", user: "alice"), progress, CancellationToken.None);
            await agent.ReplyAsync(Request("bob の 1 回目", user: "bob"), progress, CancellationToken.None);   //同じ conversation "c1"
            await agent.ReplyAsync(Request("alice の 2 回目", user: "alice"), progress, CancellationToken.None);

            Assert.That(client.Calls[1].Count, Is.EqualTo(2), "bob には alice の履歴が渡らない (system + user)");
            Assert.That(client.Calls[2].Select(m => m.Text), Is.EqualTo(new[] { client.Calls[2][0].Text, "alice の 1 回目", "A", "alice の 2 回目" }));
            Assert.That(agent.ConversationCount, Is.EqualTo(2));
        }

        //サーバーの履歴が消えた後 (保持期限・再起動) にクライアントが持っている会話の写しを送ると、それが履歴の代わりになる。履歴があるときは無視する
        [Test]
        public async Task 履歴が無いときはクライアントの写しで文脈を復元し履歴があれば写しは無視する()
        {
            var client = new FakeChatClient().Text("2 回目の答え").Text("3 回目の答え");
            var agent = new ChatClientAgent(() => client, new ChatClientAgentOptions { StreamPartialReplies = false });
            var transcript = new[]
            {
                new Codeer.LowCode.Blazor.Extras.AIChat.AIChatTranscriptMessage { IsUser = true, Text = "最初の質問" },
                new Codeer.LowCode.Blazor.Extras.AIChat.AIChatTranscriptMessage { IsUser = false, Text = "最初の答え" },
            };
            await agent.ReplyAsync(new AIChatAgentRequest { ConversationId = "c1", Message = "2 回目の質問", Transcript = transcript }, new Progress(), default);
            var texts = client.Calls[0].Select(m => m.Text).ToList();
            Assert.That(texts, Does.Contain("最初の質問"), "写しのユーザー発言が履歴として入る");
            Assert.That(texts, Does.Contain("最初の答え"), "写しの返事が履歴として入る");
            Assert.That(client.Calls[0].Count(m => m.Role == ChatRole.Assistant && m.Text == "最初の答え"), Is.EqualTo(1));
            Assert.That(texts.IndexOf("最初の質問"), Is.LessThan(texts.IndexOf("2 回目の質問")), "写しは新しい発言より前");

            var stale = new[] { new Codeer.LowCode.Blazor.Extras.AIChat.AIChatTranscriptMessage { IsUser = true, Text = "古い写し" } };
            await agent.ReplyAsync(new AIChatAgentRequest { ConversationId = "c1", Message = "3 回目の質問", Transcript = stale }, new Progress(), default);
            var texts2 = client.Calls[1].Select(m => m.Text).ToList();
            Assert.That(texts2, Does.Not.Contain("古い写し"), "履歴があるので写しは使わない");
            Assert.That(texts2, Does.Contain("2 回目の答え"), "サーバーの履歴が使われる");
        }

        [Test]
        public async Task 所有者が空のときは会話IDだけで履歴を引く()
        {
            var client = new FakeChatClient().Text("A").Text("B");
            var agent = new ChatClientAgent(() => client, new ChatClientAgentOptions { StreamPartialReplies = false });
            await agent.ReplyAsync(Request("1", user: ""), new Progress(), CancellationToken.None);
            await agent.ReplyAsync(Request("2", user: ""), new Progress(), CancellationToken.None);
            Assert.That(client.Calls[1].Count, Is.EqualTo(4), "system + 1 + A + 2");
        }

        [Test]
        public async Task 古いターンからはツール呼び出しと結果が落ちて文章だけ残る()
        {
            var client = new FakeChatClient();
            //3 ターンともツールを 1 回呼ぶ
            for (var i = 0; i < 3; i++) client.Call("echo", new Dictionary<string, object?> { ["text"] = "t" + i }, "call" + i).Text("r" + i);
            client.Text("final");
            var options = new ChatClientAgentOptions { StreamPartialReplies = false, KeepToolResultsForTurns = 1 };
            options.ToolSets.Add(new EchoToolSet());
            var agent = new ChatClientAgent(() => client, options);
            for (var i = 0; i < 3; i++) await agent.ReplyAsync(Request("q" + i), new Progress(), CancellationToken.None);
            await agent.ReplyAsync(Request("q3"), new Progress(), CancellationToken.None);

            //4 回目の送信に含まれる履歴: q0 r0 / q1 r1 (ツールなし) / q2 + 呼び出し + 結果 + r2 (直近 1 ターンはツール込み) / q3
            var sent = client.Calls.Last();
            var toolTurns = sent.Where(m => m.Contents.Any(c => c is FunctionCallContent or FunctionResultContent)).ToList();
            Assert.That(toolTurns.All(m => m.Contents.OfType<FunctionCallContent>().Any(c => c.CallId == "call2") || m.Contents.OfType<FunctionResultContent>().Any(c => c.CallId == "call2")), Is.True,
                "残っているツール呼び出し・結果は直近ターン (call2) のものだけ");
            Assert.That(sent.Select(m => m.Text).Where(t => t.StartsWith("r")), Is.EqualTo(new[] { "r0", "r1", "r2" }), "文章はすべて残る");
            Assert.That(sent.Count(m => m.Role == ChatRole.Tool), Is.EqualTo(1));
        }

        [Test]
        public async Task 文字数の上限を超えると古いターンから捨てる()
        {
            var client = new FakeChatClient();
            for (var i = 0; i < 4; i++) client.Text(new string((char)('a' + i), 1000));
            var agent = new ChatClientAgent(() => client, new ChatClientAgentOptions { StreamPartialReplies = false, MaxHistoryCharacters = 2500 });
            for (var i = 0; i < 4; i++) await agent.ReplyAsync(Request("q" + i), new Progress(), CancellationToken.None);

            //4 回目の送信: 直近の 2 ターン (bbbb… / cccc…) だけが残り、合計 2500 文字以内に収まる
            var sent = client.Calls[3];
            var assistantTexts = sent.Where(m => m.Role == ChatRole.Assistant).Select(m => m.Text[0]).ToList();
            Assert.That(assistantTexts, Is.EqualTo(new[] { 'b', 'c' }));
        }
    }
}
