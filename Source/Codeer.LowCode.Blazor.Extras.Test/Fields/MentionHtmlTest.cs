using Codeer.LowCode.Blazor.Extras.Fields;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>RichTextField の値 (HTML) からメンションを読む (スクリプトの MentionedIds / MentionsAll の中身)。</summary>
    public class MentionHtmlTest
    {
        [Test]
        public void 人のIdを本文の順に重複なしで返す()
        {
            var html = "<div>こんにちは <span class=\"mention\" data-mention-id=\"12\">@山田</span>&nbsp;と "
                + "<span data-mention-id=\"3\" class=\"mention\">@佐藤</span> と <b><span class=\"mention\" data-mention-id=\"12\">@山田</span></b></div>";
            Assert.That(MentionHtml.GetMentionedIds(html), Is.EqualTo(new[] { "12", "3" }));
            Assert.That(MentionHtml.MentionsAll(html), Is.False);
        }

        [Test]
        public void 全員を見分ける()
        {
            var html = "<p><span class=\"mention\" data-mention-all=\"\">@全員</span>&nbsp;確認お願いします</p>";
            Assert.That(MentionHtml.MentionsAll(html), Is.True);
            Assert.That(MentionHtml.GetMentionedIds(html), Is.Empty);
        }

        [Test]
        public void 属性の値は文字参照を戻す()
        {
            Assert.That(MentionHtml.GetMentionedIds("<span class='mention' data-mention-id='a&amp;b'>@x</span>"), Is.EqualTo(new[] { "a&b" }));
        }

        [Test]
        public void 似た名前の属性や本文の文字はメンションではない()
        {
            var html = "<span data-mention-identity=\"1\">x</span><span data-mention-allx=\"\">y</span><p>data-mention-id=\"9\"</p>";
            Assert.That(MentionHtml.GetMentionedIds(html), Is.Empty);
            Assert.That(MentionHtml.MentionsAll(html), Is.False);
        }

        [Test]
        public void 空の値()
        {
            Assert.That(MentionHtml.GetMentionedIds(null), Is.Empty);
            Assert.That(MentionHtml.MentionsAll(string.Empty), Is.False);
        }
    }
}
