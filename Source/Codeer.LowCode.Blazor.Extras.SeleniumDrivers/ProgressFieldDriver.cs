using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class ProgressFieldDriver : ComponentBase
    {
        /// <summary>バー / メーターどちらのルート要素も title に "12.5%" 形式の値を持つ。</summary>
        public IWebElement Root => ByCssSelector(".progress-bar, .progress-meter").Wait().Find();
        public string Text => Root.GetAttribute("title") ?? string.Empty;
        public double Percent => double.Parse(Text.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
        public bool IsMeter => Element.FindElements(By.CssSelector(".progress-meter")).Count > 0;
        public ProgressFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator ProgressFieldDriver(ElementFinder finder) => finder.Find<ProgressFieldDriver>();
    }
}
