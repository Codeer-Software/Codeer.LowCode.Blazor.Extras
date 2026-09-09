using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using System.Collections.Concurrent;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// AIChatJobStore のテスト用 Agent (Example の DummyAIChatAgent と同じ振る舞いの縮小版)。
    /// 発言に含む語で分岐: "html" → HTML / "text" → テキスト / "error" → 例外 / "slow" → 40 秒かかる。他は Markdown。
    /// 会話ごとに回数を数え "(n 回目)" を返事に入れる。途中経過と部分的な返事も流す。
    /// </summary>
    public class FakeAIChatAgent : IAIChatAgent
    {
        readonly ConcurrentDictionary<string, int> _turns = new();

        public TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(20);

        public List<AIChatAgentRequest> Requests { get; } = new();

        public async Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request);
            var turn = _turns.AddOrUpdate(request.ConversationId, 1, (_, n) => n + 1);
            var lower = request.Message.ToLowerInvariant();

            progress.Report("読んでいます…");
            await Task.Delay(StepDelay, cancellationToken);
            if (lower.Contains("error")) throw new InvalidOperationException("意図的な失敗");
            if (lower.Contains("slow"))
            {
                for (var i = 1; i <= 40; i++)
                {
                    progress.Report($"処理中 {i}/40");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
            progress.Report(string.Empty);

            if (lower.Contains("html"))
                return AIChatReply.Html($"<div style=\"color:green\">HTML の返事 ({turn} 回目) <a href=\"https://example.com\">link</a></div>");
            if (lower.Contains("text"))
                return AIChatReply.Text($"テキストの返事 ({turn} 回目)\n<b>タグ</b> はそのまま見える");

            var markdown = $"### 返事 ({turn} 回目)\n\nあなたの発言: {request.Message}\n\n| 列 | 値 |\n|---|---|\n| turn | {turn} |\n";
            for (var i = 1; i < 4; i++)
            {
                progress.ReportPartial(AIChatReply.Markdown(markdown[..(markdown.Length * i / 4)]));
                await Task.Delay(StepDelay / 2, cancellationToken);
            }
            return AIChatReply.Markdown(markdown);
        }
    }
}
