using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class BulkMailFieldDriver : ComponentBase
    {
        public ButtonDriver Send => ByCssSelector("[data-system='bulk-mail'] button:has(.bi-send)").Wait();
        /// <summary>ShowPreviewButton=true のときだけ存在する。</summary>
        public ButtonDriver Preview => ByCssSelector("[data-system='bulk-mail'] button:has(.bi-file-earmark-arrow-down)").Wait();
        public BulkMailFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator BulkMailFieldDriver(ElementFinder finder) => finder.Find<BulkMailFieldDriver>();
    }
}
