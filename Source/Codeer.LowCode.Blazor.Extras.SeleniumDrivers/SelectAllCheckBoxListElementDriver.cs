using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>ListField のヘッダに置く全選択チェックボックス (SelectAllCheckBoxListElement)。</summary>
    public class SelectAllCheckBoxListElementDriver : ComponentBase
    {
        public CheckBoxDriver CheckBox => ByCssSelector("input[type='checkbox']").Wait();
        public SelectAllCheckBoxListElementDriver(IWebElement element) : base(element) { }
        public static implicit operator SelectAllCheckBoxListElementDriver(ElementFinder finder) => finder.Find<SelectAllCheckBoxListElementDriver>();
    }
}
