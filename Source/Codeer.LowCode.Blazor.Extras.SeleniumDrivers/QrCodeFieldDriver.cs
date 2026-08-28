using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class QrCodeFieldDriver : ComponentBase
    {
        public IWebElement Image => ByCssSelector("img.qrcode-image").Wait().Find();
        public bool HasImage => Element.FindElements(By.CssSelector("img.qrcode-image")).Count > 0;
        /// <summary>data URL (PNG)。</summary>
        public string ImageSource => Image.GetAttribute("src") ?? string.Empty;
        public QrCodeFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator QrCodeFieldDriver(ElementFinder finder) => finder.Find<QrCodeFieldDriver>();
    }
}
