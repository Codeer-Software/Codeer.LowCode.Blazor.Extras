using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>EnterFocusMoveField は Enter キーでフォーカス移動する挙動だけを追加し、見える UI を持たない。</summary>
    public class EnterFocusMoveFieldDriver : ComponentBase
    {

        public EnterFocusMoveFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator EnterFocusMoveFieldDriver(ElementFinder finder) => finder.Find<EnterFocusMoveFieldDriver>();
    }
}
