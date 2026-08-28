using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class ColorPickerFieldDriver : ComponentBase
    {
        /// <summary>編集時の input[type=color]。ブラウザのネイティブ UI は操作できないので <see cref="Edit"/> を使う。</summary>
        public IWebElement Input => ByCssSelector("input.colorpicker-input").Wait().Find();
        /// <summary>表示中の色文字列 (#rrggbb)。編集時・閲覧時ともに取れる。</summary>
        public string Text => ByCssSelector(".colorpicker-text").Wait().Find().TextContent();
        public string Value => Input.GetAttribute("value") ?? string.Empty;
        /// <summary>#rrggbb を設定する。</summary>
        public void Edit(string hexColor) => Input.SetValueAndChange(hexColor);
        public ColorPickerFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator ColorPickerFieldDriver(ElementFinder finder) => finder.Find<ColorPickerFieldDriver>();
    }
}
