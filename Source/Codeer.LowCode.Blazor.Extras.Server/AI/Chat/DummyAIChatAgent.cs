using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// AI を呼ばないダミーの Agent。AIChatField の UI (考え中・途中経過・逐次表示・Markdown / HTML / テキストの表示・
    /// エラー・中断) を、AI の設定なしで確認するためのもの。本番では <see cref="IAIChatAgent"/> を実装して差し替える。
    /// 発言に含む語で振る舞いが変わる: "html" → HTML を返す / "text" → プレーンテキスト / "error" → 失敗 / "slow" → 40 秒かかる。
    /// </summary>
    public class DummyAIChatAgent : IAIChatAgent
    {
        readonly ConcurrentDictionary<string, int> _turns = new();

        /// <summary>1 ステップの待ち。テストでは短くする。</summary>
        public TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(700);

        public async Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
        {
            var turn = _turns.AddOrUpdate(request.ConversationId, 1, (_, n) => n + 1);
            var message = request.Message ?? string.Empty;
            var lower = message.ToLowerInvariant();

            progress.Report("入力を読んでいます…");
            await Task.Delay(StepDelay, cancellationToken);

            if (lower.Contains("error")) throw new InvalidOperationException("ダミー Agent: 意図的な失敗です (発言に error が含まれていました)。");

            progress.Report("整理しています…");
            await Task.Delay(StepDelay, cancellationToken);

            if (lower.Contains("slow"))
            {
                for (var i = 1; i <= 40; i++)
                {
                    progress.Report($"時間のかかる処理中… ({i}/40)");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }

            if (lower.Contains("html")) return await StreamAsync(AIChatReply.Html(BuildHtml(message, turn)), progress, cancellationToken);
            if (lower.Contains("text")) return await StreamAsync(AIChatReply.Text(BuildText(message, turn)), progress, cancellationToken);
            return await StreamAsync(AIChatReply.Markdown(BuildMarkdown(message, turn)), progress, cancellationToken);
        }

        //確定した返事を数回に分けて途中経過として流し、逐次表示を模す (HTML はタグが途切れるので分割しない)
        async Task<AIChatReply> StreamAsync(AIChatReply final, IAIChatProgress progress, CancellationToken token)
        {
            progress.Report(string.Empty);
            if (final.Format != AIChatReplyFormat.Html)
            {
                const int chunks = 6;
                var content = final.Content;
                for (var i = 1; i < chunks; i++)
                {
                    var length = content.Length * i / chunks;
                    progress.ReportPartial(new AIChatReply { Content = content[..length], Format = final.Format });
                    await Task.Delay(StepDelay / 2, token);
                }
            }
            return final;
        }

        static string BuildMarkdown(string message, int turn)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### ダミー Agent の返事 ({turn} 回目)");
            sb.AppendLine();
            sb.AppendLine($"「{message.Trim()}」と受け取りました。AI には接続していないので、表示の確認用に Markdown を返します。");
            sb.AppendLine();
            sb.AppendLine("| 語 | 振る舞い |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| `html` | HTML で返す |");
            sb.AppendLine("| `text` | プレーンテキストで返す |");
            sb.AppendLine("| `error` | 失敗する |");
            sb.AppendLine("| `slow` | 40 秒かかる (停止ボタンの確認用) |");
            sb.AppendLine();
            sb.AppendLine("- 箇条書き 1");
            sb.AppendLine("- 箇条書き 2");
            sb.AppendLine("  1. 入れ子の番号付き");
            sb.AppendLine("  2. 二つ目");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine("void Chat_OnReplyReceived(string replyHtml)");
            sb.AppendLine("{");
            sb.AppendLine("    Memo.Value = replyHtml;");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("> 引用の見え方。リンクは別タブで開きます: [Codeer](https://www.codeer.co.jp/)");
            return sb.ToString();
        }

        static string BuildText(string message, int turn)
            => $"ダミー Agent の返事 ({turn} 回目)\n\n「{message.Trim()}」をテキストで返しています。\n改行はそのまま表示され、<b>タグ</b> はエスケープされます。";

        static string BuildHtml(string message, int turn)
        {
            var encoded = WebUtility.HtmlEncode(message.Trim());
            return $$"""
                <h3 style="margin-top:0">ダミー Agent の返事 ({{turn}} 回目)</h3>
                <p>「{{encoded}}」を <b>HTML</b> で返しています。Agent が見やすいと判断した表現はそのまま表示されます。</p>
                <div style="display:flex;gap:8px;flex-wrap:wrap">
                  <div style="flex:1;min-width:120px;padding:8px 12px;border-radius:8px;background:#e7f1ff"><div style="font-size:.75em;color:#555">受注</div><div style="font-size:1.4em;font-weight:600">128 件</div></div>
                  <div style="flex:1;min-width:120px;padding:8px 12px;border-radius:8px;background:#e8f5e9"><div style="font-size:.75em;color:#555">出荷済</div><div style="font-size:1.4em;font-weight:600">97 件</div></div>
                  <div style="flex:1;min-width:120px;padding:8px 12px;border-radius:8px;background:#fff3e0"><div style="font-size:.75em;color:#555">遅延</div><div style="font-size:1.4em;font-weight:600;color:#c62828">4 件</div></div>
                </div>
                <p style="margin-top:8px"><a href="https://www.codeer.co.jp/">リンク</a> にも target が付きます。</p>
                """;
        }
    }
}
