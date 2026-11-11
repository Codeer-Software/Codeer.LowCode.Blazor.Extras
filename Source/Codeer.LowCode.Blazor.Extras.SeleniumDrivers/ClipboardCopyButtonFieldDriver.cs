using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class ClipboardCopyButtonFieldDriver : ComponentBase
    {
        public ButtonDriver Button => ByCssSelector("button[data-system='clipboard-copy']").Wait();
        public ClipboardCopyButtonFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator ClipboardCopyButtonFieldDriver(ElementFinder finder) => finder.Find<ClipboardCopyButtonFieldDriver>();
    }
}
