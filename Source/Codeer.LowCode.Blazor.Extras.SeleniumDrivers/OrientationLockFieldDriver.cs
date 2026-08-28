using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class OrientationLockFieldDriver : ComponentBase
    {
        /// <summary>向きが合わないときだけ CSS で表示されるオーバーレイ。</summary>
        public IWebElement Overlay => ByCssSelector(".extras-orientation-overlay").Wait().Find();
        public string Message => ByCssSelector(".extras-orientation-overlay__message").Wait().Find().TextContent();
        public bool IsOverlayVisible => Overlay.Displayed;
        public OrientationLockFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator OrientationLockFieldDriver(ElementFinder finder) => finder.Find<OrientationLockFieldDriver>();
    }
}
