using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>Agent の返事 (テキスト / Markdown / HTML) を AIChatField 向けの HTML に揃える変換。</summary>
    public class ChatReplyHtmlTest
    {
        [Test]
        public void テキストはエスケープして段落と改行にする()
        {
            var html = ChatReplyHtml.FromText("1行目\n2行目\n\n<b>タグ</b>");
            Assert.That(html, Does.Contain("<p>1行目<br>2行目</p>"));
            Assert.That(html, Does.Contain("<p>&lt;b&gt;タグ&lt;/b&gt;</p>"));
        }

        [Test]
        public void Markdownは表とコードとリンクを変換しリンクは別タブで開く()
        {
            var md = "## 見出し\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```\n\n[Codeer](https://www.codeer.co.jp/)";
            var html = ChatReplyHtml.FromMarkdown(md);
            Assert.That(html, Does.Contain("<h2"));
            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Contain("<code class=\"language-csharp\">"));
            Assert.That(html, Does.Contain("<a href=\"https://www.codeer.co.jp/\" target=\"_blank\" rel=\"noopener noreferrer\">"));
        }

        [Test]
        public void Markdownの単独改行は改行タグになる()
        {
            var html = ChatReplyHtml.FromMarkdown("1行目\n2行目");
            Assert.That(html, Does.Contain("<br"));
        }

        [Test]
        public void HTMLは加工せず通す()
        {
            var src = "<div style=\"color:red\"><p>そのまま</p><a href=\"#top\">頁内</a><a href=\"https://x\" target=\"_self\">既定</a></div>";
            var html = ChatReplyHtml.FromHtml(src);
            Assert.That(html, Does.Contain("<div style=\"color:red\"><p>そのまま</p>"));
            //ページ内リンクと target 指定済みには付けない
            Assert.That(html, Does.Contain("<a href=\"#top\">"));
            Assert.That(html, Does.Contain("target=\"_self\""));
            Assert.That(html, Does.Not.Contain("_blank"));
        }

        [Test]
        public void Autoは先頭がタグならHTMLそれ以外はMarkdown()
        {
            Assert.That(ChatReplyHtml.LooksLikeHtml("  <p>x</p>"), Is.True);
            Assert.That(ChatReplyHtml.LooksLikeHtml("<br/>x"), Is.True);
            Assert.That(ChatReplyHtml.LooksLikeHtml("a < b で <p> を含む"), Is.False);
            Assert.That(ChatReplyHtml.LooksLikeHtml("# 見出し"), Is.False);

            Assert.That(ChatReplyHtml.Normalize(new AIChatReply { Content = "<p>x</p>" }), Is.EqualTo("<p>x</p>"));
            Assert.That(ChatReplyHtml.Normalize(new AIChatReply { Content = "**太字**" }), Does.Contain("<strong>太字</strong>"));
            Assert.That(ChatReplyHtml.Normalize(AIChatReply.Text("<p>x</p>")), Does.Contain("&lt;p&gt;"));
        }

        [Test]
        public void 空は空()
        {
            Assert.That(ChatReplyHtml.FromText(""), Is.Empty);
            Assert.That(ChatReplyHtml.FromMarkdown("  "), Is.Empty);
            Assert.That(ChatReplyHtml.FromHtml(""), Is.Empty);
        }
    }
}
