using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>承認履歴の1行。</summary>
    public class ApprovalHistoryEntryDriver : ComponentBase
    {
        public string When => ByCssSelector(".approval-history-when").Wait().Find().TextContent();
        public string Action => ByCssSelector(".approval-history-action").Wait().Find().TextContent();
        public string Actor => ByCssSelector(".approval-history-actor").Wait().Find().TextContent();
        /// <summary>コメントが無ければ空文字。</summary>
        public string Comment
        {
            get
            {
                var e = Element.FindElements(By.CssSelector(".approval-history-comment"));
                return e.Count == 0 ? string.Empty : e[0].TextContent();
            }
        }
        public ApprovalHistoryEntryDriver(IWebElement element) : base(element) { }
        public static implicit operator ApprovalHistoryEntryDriver(ElementFinder finder) => finder.Find<ApprovalHistoryEntryDriver>();
    }

    /// <summary>承認履歴 (ApprovalHistoryField 単体、および ApprovalFlowField 内の履歴部)。</summary>
    public class ApprovalHistoryDriver : ComponentBase
    {
        public string Title => ByCssSelector(".approval-history-title").Wait().Find().TextContent();
        public ItemsControlDriver<ApprovalHistoryEntryDriver> Entries => ByCssSelector(".approval-history > ul").Wait().Find<ItemsControlDriver<ApprovalHistoryEntryDriver>>();
        public bool HasEntries => Element.FindElements(By.CssSelector(".approval-history > ul > li")).Count > 0;
        public ApprovalHistoryDriver(IWebElement element) : base(element) { }
        public static implicit operator ApprovalHistoryDriver(ElementFinder finder) => finder.Find<ApprovalHistoryDriver>();
    }

    public class ApprovalHistoryFieldDriver : ComponentBase
    {
        /// <summary>履歴が 0 件のときは何も描画されない。</summary>
        public bool IsVisible => Element.FindElements(By.CssSelector("[data-system='approval-history']")).Count > 0;
        public ApprovalHistoryDriver History => ByCssSelector("[data-system='approval-history']").Wait();
        public ApprovalHistoryFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator ApprovalHistoryFieldDriver(ElementFinder finder) => finder.Find<ApprovalHistoryFieldDriver>();
    }
}
