using Codeer.LowCode.Blazor.Extras.Markdown;

namespace Codeer.LowCode.Blazor.Extras.Test.Markdown
{
    /// <summary>MarkdownField の描画: 生 HTML の無効化、表・コード・リンク、単独改行、プレーンテキスト化。</summary>
    public class MarkdownRendererTest
    {
        [Test]
        public void 生のHTMLは無効化されて文字として見える()
        {
            var html = MarkdownRenderer.ToHtml("<script>alert(1)</script> と <b>太字</b>");
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Not.Contain("<b>"));
            Assert.That(html, Does.Contain("&lt;script&gt;"));
        }

        [Test]
        public void 表とコードとリンクを描く_リンクは別タブ()
        {
            var md = "## 見出し\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```\n\n[Codeer](https://www.codeer.co.jp/)";
            var html = MarkdownRenderer.ToHtml(md);
            Assert.That(html, Does.Contain("<h2"));
            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Contain("<code class=\"language-csharp\">"));
            Assert.That(html, Does.Contain("<a href=\"https://www.codeer.co.jp/\" target=\"_blank\" rel=\"noopener noreferrer\">"));
        }

        [Test]
        public void 単独の改行は改行タグになる()
        {
            Assert.That(MarkdownRenderer.ToHtml("1行目\n2行目"), Does.Contain("<br"));
        }

        [Test]
        public void チェックリストを描く()
        {
            var html = MarkdownRenderer.ToHtml("- [ ] 未\n- [x] 済");
            Assert.That(html, Does.Contain("type=\"checkbox\""));
            Assert.That(html, Does.Contain("checked"));
        }

        [Test]
        public void ページ内リンクとtarget指定済みには付けない()
        {
            var html = MarkdownRenderer.ToHtml("[上へ](#top)");
            Assert.That(html, Does.Contain("<a href=\"#top\">"));
        }

        [Test]
        public void プレーンテキスト化は記法を落とす()
        {
            var text = MarkdownRenderer.ToPlainText("## 見出し\n\n**太字** と [リンク](https://x/)");
            Assert.That(text, Does.Contain("見出し"));
            Assert.That(text, Does.Contain("太字"));
            Assert.That(text, Does.Contain("リンク"));
            Assert.That(text, Does.Not.Contain("**"));
            Assert.That(text, Does.Not.Contain("##"));
            Assert.That(text, Does.Not.Contain("https://x/"));
        }

        [Test]
        public void 空は空()
        {
            Assert.That(MarkdownRenderer.ToHtml(null), Is.Empty);
            Assert.That(MarkdownRenderer.ToHtml("  "), Is.Empty);
            Assert.That(MarkdownRenderer.ToPlainText(null), Is.Empty);
        }
    }
}
