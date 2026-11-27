using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class RichTextFieldDriver : ComponentBase
    {
        /// <summary>編集用の contenteditable 要素。</summary>
        public IWebElement Editor => ByCssSelector(".richtext-content").Wait().Find();
        /// <summary>閲覧専用時の表示要素。</summary>
        public IWebElement View => ByCssSelector(".richtext-view").Wait().Find();
        public bool IsViewOnly => Element.FindElements(By.CssSelector(".richtext-view")).Count > 0;
        /// <summary>現在の HTML (編集/閲覧どちらでも)。</summary>
        public string Html => (IsViewOnly ? View : Editor).GetAttribute("innerHTML") ?? string.Empty;
        public string Text => (IsViewOnly ? View : Editor).TextContent();

        public ButtonDriver Bold => ByCssSelector(".richtext-toolbar-btn-bold").Wait();
        public ButtonDriver Italic => ByCssSelector(".richtext-toolbar-btn-italic").Wait();
        public ButtonDriver Underline => ByCssSelector(".richtext-toolbar-btn-underline").Wait();
        public ButtonDriver Strikethrough => ByCssSelector(".richtext-toolbar-btn-strikethrough").Wait();
        public DropDownListDriver Heading => ByCssSelector(".richtext-toolbar-select").Wait();
        public ButtonDriver UnorderedList => ByCssSelector(".richtext-toolbar-btn-ul").Wait();
        public ButtonDriver OrderedList => ByCssSelector(".richtext-toolbar-btn-ol").Wait();
        public ButtonDriver AlignLeft => ByCssSelector(".richtext-toolbar-btn-align-left").Wait();
        public ButtonDriver AlignCenter => ByCssSelector(".richtext-toolbar-btn-align-center").Wait();
        public ButtonDriver AlignRight => ByCssSelector(".richtext-toolbar-btn-align-right").Wait();
        public ButtonDriver ForeColor => ByCssSelector(".richtext-toolbar-btn-forecolor").Wait();
        public ButtonDriver BackColor => ByCssSelector(".richtext-toolbar-btn-backcolor").Wait();
        public ButtonDriver Link => ByCssSelector(".richtext-toolbar-btn-link").Wait();
        public ButtonDriver ClearFormat => ByCssSelector(".richtext-toolbar-btn-clear").Wait();
        public ButtonDriver Undo => ByCssSelector(".richtext-toolbar-btn-undo").Wait();
        public ButtonDriver Redo => ByCssSelector(".richtext-toolbar-btn-redo").Wait();
        /// <summary>ForeColor / BackColor を押した後に開くパレットの色見本。</summary>
        public ItemsControlDriver<ButtonDriver> ColorSwatches => ByCssSelector(".richtext-color-palette").Wait().Find<ItemsControlDriver<ButtonDriver>>();
        public TextBoxDriver LinkUrl => ByCssSelector(".richtext-link-input").Wait();
        public ButtonDriver LinkOk => ByCssSelector(".richtext-link-popup-ok").Wait();
        public ButtonDriver LinkCancel => ByCssSelector(".richtext-link-popup-cancel").Wait();

        /// <summary>エディタにフォーカスしてキー入力する (既存内容の末尾に追記)。</summary>
        public void TypeText(string text)
        {
            Editor.Click();
            Editor.SendKeys(text);
        }

        /// <summary>全選択して打ち直す。</summary>
        public void Edit(string text)
        {
            Editor.Click();
            Editor.SendKeys(Keys.Control + "a");
            Editor.SendKeys(Keys.Delete);
            Editor.SendKeys(text);
        }

        /// <summary>@ を打って出ている候補 (上から順)。出ていなければ空。</summary>
        public IReadOnlyList<RichTextMentionCandidate> MentionCandidates
            => Element.FindElements(By.CssSelector(".richtext-mention-popup .richtext-mention-item")).Select(ToCandidate).ToList();

        /// <summary>候補の枠が出ているか。</summary>
        public bool IsMentionPopupVisible => Element.FindElements(By.CssSelector(".richtext-mention-popup")).Count > 0;

        /// <summary>本文に入っているメンション (本文の順。編集/閲覧どちらでも)。</summary>
        public IReadOnlyList<RichTextMention> Mentions
            => (IsViewOnly ? View : Editor).FindElements(By.CssSelector("span.mention")).Select(ToMention).ToList();

        /// <summary>
        /// 本文の末尾に @ と文字を打ち、候補の枠が出るまで待つ (選ばない)。
        /// @ は行頭か空白の後でないとメンションにならないので、本文が空白で終わっていなければ空白を 1 つ入れる。
        /// 候補が 1 つも無い文字だと枠は出ないので、そのときは TypeText で打つ。
        /// </summary>
        public void TypeMention(string text = "", int timeoutSeconds = 10)
        {
            Editor.Click();
            Editor.SendKeys(Keys.Control + Keys.End);
            var current = Editor.GetAttribute("textContent") ?? string.Empty;
            if (current.Length > 0 && !char.IsWhiteSpace(current[^1])) Editor.SendKeys(" ");
            Editor.SendKeys("@" + text);
            WaitFor(() => IsMentionPopupVisible, timeoutSeconds, "the mention candidates did not appear.");
        }

        /// <summary>本文の末尾に @ と文字を打ち、表示名が一致する候補をクリックして、本文に入るまで待つ。</summary>
        public void PickMention(string text, string displayName, int timeoutSeconds = 10)
        {
            var count = Mentions.Count;
            TypeMention(text, timeoutSeconds);
            WaitFor(() => MentionCandidates.Any(e => e.DisplayName == displayName), timeoutSeconds, "the mention candidate did not appear.");
            Element.FindElements(By.CssSelector(".richtext-mention-popup .richtext-mention-item"))
                .First(e => ToCandidate(e).DisplayName == displayName).Click();
            WaitFor(() => Mentions.Count > count && !IsMentionPopupVisible, timeoutSeconds, "the mention was not inserted.");
        }

        static RichTextMentionCandidate ToCandidate(IWebElement item)
        {
            var id = item.GetAttribute("data-mention-candidate") ?? string.Empty;
            var isAll = item.HasClass("richtext-mention-item-all");
            return new RichTextMentionCandidate(
                isAll ? string.Empty : id,
                item.FindElement(By.CssSelector(".richtext-mention-name")).TextContent(),
                item.FindElements(By.CssSelector(".richtext-mention-sub")).FirstOrDefault()?.TextContent() ?? string.Empty,
                isAll,
                item.HasClass("active"));
        }

        static RichTextMention ToMention(IWebElement span)
            => new(span.GetAttribute("data-mention-id") ?? string.Empty, span.TextContent(), span.GetAttribute("data-mention-all") != null);

        static void WaitFor(Func<bool> condition, int timeoutSeconds, string message)
        {
            var end = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < end)
            {
                if (condition()) return;
                Thread.Sleep(200);
            }
            throw new TimeoutException("RichTextField: " + message);
        }

        public RichTextFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator RichTextFieldDriver(ElementFinder finder) => finder.Find<RichTextFieldDriver>();
    }

    /// <summary>
    /// メンションの候補 1 行。Id は行の Id (「全員」は空)、DisplayNameSub は補助の表示名 (出ていなければ空)、
    /// IsActive は選択中 (↑↓ やマウスで選んでいる行) か。
    /// </summary>
    public record RichTextMentionCandidate(string Id, string DisplayName, string DisplayNameSub, bool IsAll, bool IsActive);

    /// <summary>本文に入っているメンション 1 つ。Id はユーザーの Id (「全員」は空)、Text は表示の文字 (@ 付き)。</summary>
    public record RichTextMention(string Id, string Text, bool IsAll);
}
