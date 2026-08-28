using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class BulkFileTransferButtonFieldDriver : ComponentBase
    {
        public ButtonDriver Download => ByCssSelector("button[data-system='bulk-download']").Wait();
        /// <summary>一括更新のファイル入力 (非表示の input[type=file])。SendKeys(パス) でアップロードする。</summary>
        public IWebElement UploadInput => ByCssSelector("[data-system='bulk-upload'] input[type='file']").Wait().Find();
        public void Upload(string filePath) => UploadInput.SendKeys(filePath);
        public BulkFileTransferButtonFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator BulkFileTransferButtonFieldDriver(ElementFinder finder) => finder.Find<BulkFileTransferButtonFieldDriver>();
    }
}
