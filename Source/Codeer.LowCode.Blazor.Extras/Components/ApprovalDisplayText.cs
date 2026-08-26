using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Properties;

namespace Codeer.LowCode.Blazor.Extras.Components
{
    /// <summary>承認アクションの表示テキスト (複数コンポーネントで共用)。</summary>
    internal static class ApprovalDisplayText
    {
        internal static string ActionText(string action) => Enum.TryParse<ApprovalAction>(action, out var a) ? a switch
        {
            ApprovalAction.Submit => Resources.ApprovalAction_Submit,
            ApprovalAction.Approve => Resources.ApprovalAction_Approve,
            ApprovalAction.Reject => Resources.ApprovalAction_Reject,
            ApprovalAction.Return => Resources.ApprovalAction_Return,
            ApprovalAction.Withdraw => Resources.ApprovalAction_Withdraw,
            ApprovalAction.Resubmit => Resources.ApprovalAction_Resubmit,
            ApprovalAction.Confirm => Resources.ApprovalAction_Confirm,
            _ => action,
        } : action;
    }
}
