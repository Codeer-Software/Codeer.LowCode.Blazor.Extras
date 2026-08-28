using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>承認ステップ内の1メンバー。</summary>
    public class ApprovalMemberDriver : ComponentBase
    {
        public string Mark => ByCssSelector(".approval-member-mark").Wait().Find().TextContent();
        public string Name => ByCssSelector(".approval-member-name").Wait().Find().TextContent();
        /// <summary>li の class "status-xxx" から取った状態 (pending / approved 等、小文字)。</summary>
        public string Status => (Element.GetAttribute("class") ?? string.Empty).Split(' ')
            .FirstOrDefault(c => c.StartsWith("status-"))?["status-".Length..] ?? string.Empty;
        public ApprovalMemberDriver(IWebElement element) : base(element) { }
        public static implicit operator ApprovalMemberDriver(ElementFinder finder) => finder.Find<ApprovalMemberDriver>();
    }

    /// <summary>承認ステップ。</summary>
    public class ApprovalStepDriver : ComponentBase
    {
        public string Name => ByCssSelector(".approval-step-name").Wait().Find().TextContent();
        public bool IsCurrent => Element.HasClass("current");
        public ItemsControlDriver<ApprovalMemberDriver> Members => ByCssSelector(".approval-members").Wait().Find<ItemsControlDriver<ApprovalMemberDriver>>();
        public ApprovalStepDriver(IWebElement element) : base(element) { }
        public static implicit operator ApprovalStepDriver(ElementFinder finder) => finder.Find<ApprovalStepDriver>();
    }

    public class ApprovalFlowFieldDriver : ComponentBase
    {
        /// <summary>未申請のときだけ表示される文言。</summary>
        public bool IsSubmitted => Element.FindElements(By.CssSelector(".approval-not-submitted")).Count == 0;
        /// <summary>フロー状態の表示文言 (申請中 / 承認済 等)。未申請時は空文字。</summary>
        public string Status
        {
            get
            {
                var e = Element.FindElements(By.CssSelector(".approval-status-badge"));
                return e.Count == 0 ? string.Empty : e[0].TextContent();
            }
        }
        /// <summary>class "approval-status-xxx" から取った状態キー (小文字)。未申請時は空文字。</summary>
        public string StatusKey
        {
            get
            {
                var e = Element.FindElements(By.CssSelector(".approval-status-badge"));
                if (e.Count == 0) return string.Empty;
                return (e[0].GetAttribute("class") ?? string.Empty).Split(' ')
                    .FirstOrDefault(c => c.StartsWith("approval-status-") && c != "approval-status-badge")?["approval-status-".Length..] ?? string.Empty;
            }
        }
        public ItemsControlDriver<ApprovalStepDriver> Steps => ByCssSelector(".approval-steps").Wait().Find<ItemsControlDriver<ApprovalStepDriver>>();
        public TextAreaDriver Comment => ByCssSelector("textarea.approval-comment").Wait();

        public ButtonDriver Submit => ByCssSelector("button[data-system='approval-submit']").Wait();
        public ButtonDriver Approve => ByCssSelector("button[data-system='approval-approve']").Wait();
        public ButtonDriver Reject => ByCssSelector("button[data-system='approval-reject']").Wait();
        public ButtonDriver Return => ByCssSelector("button[data-system='approval-return']").Wait();
        public ButtonDriver Confirm => ByCssSelector("button[data-system='approval-confirm']").Wait();
        public ButtonDriver Withdraw => ByCssSelector("button[data-system='approval-withdraw']").Wait();
        public ButtonDriver Resubmit => ByCssSelector("button[data-system='approval-resubmit']").Wait();
        /// <summary>現在表示されている操作ボタン。</summary>
        public ItemsControlDriver<ButtonDriver> Actions => ByCssSelector(".approval-actions").Wait().Find<ItemsControlDriver<ButtonDriver>>();
        public bool HasAction(string dataSystem) => Element.FindElements(By.CssSelector($"button[data-system='{dataSystem}']")).Count > 0;

        public bool HasHistory => Element.FindElements(By.CssSelector(".approval-history")).Count > 0;
        public ApprovalHistoryDriver History => ByCssSelector(".approval-history").Wait();

        public ApprovalFlowFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator ApprovalFlowFieldDriver(ElementFinder finder) => finder.Find<ApprovalFlowFieldDriver>();
    }
}
