using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>PasswordHashField は DB 保存用のハッシュ列で UI を持たない。</summary>
    public class PasswordHashFieldDriver : ComponentBase
    {

        public PasswordHashFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator PasswordHashFieldDriver(ElementFinder finder) => finder.Find<PasswordHashFieldDriver>();
    }
}
