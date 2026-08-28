using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>FileColumnMappingField は設定用フィールドで実行時に何も描画しない。</summary>
    public class FileColumnMappingFieldDriver : ComponentBase
    {

        public FileColumnMappingFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator FileColumnMappingFieldDriver(ElementFinder finder) => finder.Find<FileColumnMappingFieldDriver>();
    }
}
