using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class MailFieldDriver : ComponentBase
    {
        public ButtonDriver Send => ByCssSelector("[data-system='mail'] button:has(.bi-send)").Wait();
        /// <summary>ShowPreviewButton=true のときだけ存在する。</summary>
        public ButtonDriver Preview => ByCssSelector("[data-system='mail'] button:has(.bi-file-earmark-arrow-down)").Wait();
        public MailFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator MailFieldDriver(ElementFinder finder) => finder.Find<MailFieldDriver>();
    }
}
