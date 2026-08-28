using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Selenium.StandardControls.TestAssistant.GeneratorToolKit;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class AITextAnalyzerFieldDriver : ComponentBase
    {
        /// <summary>解析するファイルの入力 (非表示の input[type=file])。SendKeys(パス) でアップロードする。</summary>
        public IWebElement FileInput => ByCssSelector("input[type='file']").Wait().Find();
        /// <summary>テキスト入力ダイアログを開くボタン。</summary>
        public ButtonDriver Text => ByCssSelector("button.btn").Wait();
        public void UploadFile(string filePath) => FileInput.SendKeys(filePath);

        public AITextAnalyzerFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator AITextAnalyzerFieldDriver(ElementFinder finder) => finder.Find<AITextAnalyzerFieldDriver>();
    }

    /// <summary>AITextAnalyzerField の Text ボタンで開くテキスト入力ダイアログ。</summary>
    public class AITextAnalyzerDialogDriver : ComponentBase
    {
        public TextAreaDriver Input => ByCssSelector(".modal-body textarea").Wait();
        /// <summary>[Analyze] [Close] の順。</summary>
        public ItemsControlDriver<ButtonDriver> Buttons => ByCssSelector(".modal-body .text-end").Wait().Find<ItemsControlDriver<ButtonDriver>>();
        public ButtonDriver Analyze => Buttons.GetItem(0);
        public ButtonDriver Close => Buttons.GetItem(1);
        public ButtonDriver CloseButton => ByCssSelector("button.btn-close").Wait();

        public AITextAnalyzerDialogDriver(IWebElement element) : base(element) { }
        public static implicit operator AITextAnalyzerDialogDriver(ElementFinder finder) => finder.Find<AITextAnalyzerDialogDriver>();
    }

    public static class AITextAnalyzerDialogExtensions
    {
        [ComponentObjectIdentify]
        public static AITextAnalyzerDialogDriver AttachAITextAnalyzerDialog(this IWebDriver driver)
            => new MappingBase(driver).ByCssSelector(".modal.show:has(.modal-body > div > textarea)").Wait();
    }
}
