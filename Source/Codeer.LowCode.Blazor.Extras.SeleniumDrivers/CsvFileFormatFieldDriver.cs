using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>CsvFileFormatField は設定用フィールドで実行時に何も描画しない。</summary>
    public class CsvFileFormatFieldDriver : ComponentBase
    {

        public CsvFileFormatFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator CsvFileFormatFieldDriver(ElementFinder finder) => finder.Find<CsvFileFormatFieldDriver>();
    }
}
