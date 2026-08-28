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
        public RichTextFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator RichTextFieldDriver(ElementFinder finder) => finder.Find<RichTextFieldDriver>();
    }
}
