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

        [TestCase("[開く](javascript:alert(1))")]
        [TestCase("[開く](JavaScript&#58;alert(1))")]
        [TestCase("[開く](java&#9;script:alert(1))")]
        [TestCase("<javascript:alert(1)>")]
        [TestCase("[開く][r]\n\n[r]: javascript:alert(1)")]
        [TestCase("![画像](javascript:alert(1))")]
        [TestCase("[開く](data:text/html,<script>alert(1)</script>)")]
        public void スクリプトが動くURLのリンクと画像はページ内リンクにする(string md)
        {
            var html = MarkdownRenderer.ToHtml(md);
            Assert.That(html.ToLowerInvariant(), Does.Not.Contain("javascript"));
            Assert.That(html, Does.Not.Contain("data:"));
            Assert.That(html, Does.Contain("\"#\""));
        }

        [Test]
        public void httpとmailtoと相対のリンクはそのまま()
        {
            var html = MarkdownRenderer.ToHtml("[a](https://x/) [b](http://x/) [c](mailto:a@example.com) [d](/Main/Order/1) [e](Order/1?x=a:b)");
            Assert.That(html, Does.Contain("href=\"https://x/\""));
            Assert.That(html, Does.Contain("href=\"http://x/\""));
            Assert.That(html, Does.Contain("href=\"mailto:a@example.com\""));
            Assert.That(html, Does.Contain("href=\"/Main/Order/1\""));
            Assert.That(html, Does.Contain("href=\"Order/1?x=a:b\""));
        }

        [Test]
        public void 電話リンクとdata形式の画像はそのまま_data形式のリンクと画像以外のdataは無効()
        {
            var html = MarkdownRenderer.ToHtml("[電話](tel:0312345678) ![図](data:image/png;base64,AAAA) ![悪](data:text/html,x)");
            Assert.That(html, Does.Contain("href=\"tel:0312345678\""));
            Assert.That(html, Does.Contain("src=\"data:image/png;base64,AAAA\""));
            Assert.That(html, Does.Not.Contain("data:text"));
        }

        [Test]
        public void 属性記法で任意の属性は付けられない()
        {
            var html = MarkdownRenderer.ToHtml("**x**{onclick=\"alert(1)\" .position-fixed}\n\n# 見出し {#other-id style=\"position:fixed\"}");
            //記法は文字として残る (引用符はエスケープされる) ので、属性として出ていないことを見る
            Assert.That(html, Does.Not.Contain("onclick=\""));
            Assert.That(html, Does.Not.Contain("class=\"position-fixed"));
            Assert.That(html, Does.Not.Contain("style=\""));
            Assert.That(html, Does.Not.Contain("id=\"other-id\""));
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
