using Markdig;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// Agent の返事 (テキスト / Markdown / HTML) を、AIChatField がそのまま表示できる HTML 断片に揃える。
    /// クライアントは HTML を表示するだけで内容を解釈しないので、変換はすべてここで行う。
    /// Agent が返した HTML は加工せず通す (サーバーは信頼境界の内側。リンクの target だけ付ける)。
    /// </summary>
    internal static class ChatReplyHtml
    {
        static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseSoftlineBreakAsHardlineBreak()
            .Build();

        //先頭 (空白を除く) がタグで始まれば HTML とみなす
        static readonly Regex _startsWithTag = new(@"^\s*<([a-zA-Z][\w-]*)(\s[^<>]*)?/?>", RegexOptions.Compiled);
        static readonly Regex _anchor = new(@"<a\b([^>]*)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hasTarget = new(@"\btarget\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hrefExternal = new(@"\bhref\s*=\s*[""']?(https?:)?//", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>形式の申告に従って HTML にする。Auto は内容から判定。</summary>
        public static string Normalize(AIChatReply reply)
            => reply.Format switch
            {
                AIChatReplyFormat.Text => FromText(reply.Content),
                AIChatReplyFormat.Markdown => FromMarkdown(reply.Content),
                AIChatReplyFormat.Html => FromHtml(reply.Content),
                _ => LooksLikeHtml(reply.Content) ? FromHtml(reply.Content) : FromMarkdown(reply.Content),
            };

        /// <summary>プレーンテキスト。エスケープし、空行で段落、改行は &lt;br&gt;。</summary>
        public static string FromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
            var sb = new StringBuilder();
            foreach (var paragraph in Regex.Split(normalized, @"\n\s*\n"))
            {
                if (paragraph.Length == 0) continue;
                sb.Append("<p>");
                sb.Append(WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>"));
                sb.Append("</p>\n");
            }
            return sb.ToString();
        }

        /// <summary>Markdown (表・タスクリスト・自動リンク等の拡張込み。単独の改行は &lt;br&gt;)。</summary>
        public static string FromMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            return AddLinkTargets(Markdig.Markdown.ToHtml(markdown, _pipeline));
        }

        /// <summary>HTML はそのまま。リンクに target="_blank" を付けるだけ。</summary>
        public static string FromHtml(string html)
            => string.IsNullOrWhiteSpace(html) ? string.Empty : AddLinkTargets(html);

        public static bool LooksLikeHtml(string content)
            => !string.IsNullOrEmpty(content) && _startsWithTag.IsMatch(content);

        /// <summary>
        /// チャットの中の外部リンク (http(s):// と //) は別タブで開く。アプリ内のリンク (/Main/Order/123 のような相対 URL) と
        /// ページ内リンク、target 指定済みはそのまま (同じタブで CLB の画面へ遷移する)。
        /// </summary>
        public static string AddLinkTargets(string html)
            => _anchor.Replace(html, m =>
            {
                var attrs = m.Groups[1].Value;
                if (_hasTarget.IsMatch(attrs) || !_hrefExternal.IsMatch(attrs)) return m.Value;
                return $"<a{attrs} target=\"_blank\" rel=\"noopener noreferrer\">";
            });
    }
}
