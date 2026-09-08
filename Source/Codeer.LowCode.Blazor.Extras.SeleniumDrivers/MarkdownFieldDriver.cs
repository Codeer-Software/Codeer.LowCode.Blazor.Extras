using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>MarkdownField。編集は textarea、閲覧・プレビューは描画された HTML。</summary>
    public class MarkdownFieldDriver : ComponentBase
    {
        /// <summary>編集欄 (Markdown テキスト)。プレビュータブ表示中は無い。</summary>
        public TextAreaDriver Input => ByCssSelector("[data-role='input']").Wait();
        /// <summary>閲覧専用時の表示要素。</summary>
        public IWebElement View => ByCssSelector("[data-role='view']").Wait().Find();
        /// <summary>編集時のプレビュー (タブでプレビューを選んだとき / 左右並び)。</summary>
        public IWebElement Preview => ByCssSelector("[data-role='preview']").Wait().Find();
        public bool IsViewOnly => Element.FindElements(By.CssSelector("[data-role='view']")).Count > 0;
        public bool HasPreview => Element.FindElements(By.CssSelector("[data-role='preview']")).Count > 0;

        /// <summary>描画結果の HTML (閲覧時は View、編集時はプレビュー)。</summary>
        public string RenderedHtml => (IsViewOnly ? View : Preview).GetAttribute("innerHTML") ?? string.Empty;

        /// <summary>タブ表示のとき: [編集] [プレビュー]。</summary>
        public ButtonDriver EditTab => ByCssSelector("[data-role='tabs'] .markdown-tab:nth-child(1)").Wait();
        public ButtonDriver PreviewTab => ByCssSelector("[data-role='tabs'] .markdown-tab:nth-child(2)").Wait();

        public ButtonDriver Heading => ByCssSelector(".markdown-tool-heading").Wait();
        public ButtonDriver Bold => ByCssSelector(".markdown-tool-bold").Wait();
        public ButtonDriver Italic => ByCssSelector(".markdown-tool-italic").Wait();
        public ButtonDriver Strikethrough => ByCssSelector(".markdown-tool-strike").Wait();
        public ButtonDriver UnorderedList => ByCssSelector(".markdown-tool-ul").Wait();
        public ButtonDriver OrderedList => ByCssSelector(".markdown-tool-ol").Wait();
        public ButtonDriver TaskList => ByCssSelector(".markdown-tool-task").Wait();
        public ButtonDriver Quote => ByCssSelector(".markdown-tool-quote").Wait();
        public ButtonDriver Link => ByCssSelector(".markdown-tool-link").Wait();
        public ButtonDriver Code => ByCssSelector(".markdown-tool-code").Wait();
        public ButtonDriver CodeBlock => ByCssSelector(".markdown-tool-codeblock").Wait();
        public ButtonDriver Table => ByCssSelector(".markdown-tool-table").Wait();

        public MarkdownFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator MarkdownFieldDriver(ElementFinder finder) => finder.Find<MarkdownFieldDriver>();
    }
}
