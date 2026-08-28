using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class ExcelReportButtonFieldDriver : ComponentBase
    {
        public ButtonDriver Button => ByCssSelector("button[data-system='excel-report']").Wait();
        public ExcelReportButtonFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator ExcelReportButtonFieldDriver(ElementFinder finder) => finder.Find<ExcelReportButtonFieldDriver>();
    }
}
