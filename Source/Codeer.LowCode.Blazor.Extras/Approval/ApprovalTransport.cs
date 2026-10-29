using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// ApprovalFlowField の操作の送り先。既定は共通の口 (<see cref="ExtrasTransport"/> = ホストの 1 本の API の ApprovalApi)。
    /// 承認専用の Controller を持つホストは <see cref="EndPointBase"/> を設定する (今までの使い方)。
    /// </summary>
    public static class ApprovalTransport
    {
        /// <summary>承認専用の API の URL (例: "/api/approval")。設定するとすべての操作をこの 1 本に POST する。空 (既定) なら共通の口を使う。</summary>
        public static string EndPointBase { get; set; } = string.Empty;

        internal static async Task<ApprovalActionResult> ExecuteAsync(IHttpService? http, ApprovalCommand command)
        {
            if (!string.IsNullOrEmpty(EndPointBase))
            {
                if (http == null) return ApprovalActionResult.Failure("Approval endpoint is not configured.");
                return await http.PostAsJsonAsync<ApprovalCommand, ApprovalActionResult>(EndPointBase, command)
                    ?? ApprovalActionResult.Failure("Approval request failed.");
            }
            if (!ExtrasTransport.CanSend(http)) return ApprovalActionResult.Failure("Approval endpoint is not configured.");
            return (await ExtrasTransport.CallAsync<ApprovalActionResult>(http, ApprovalApiNames.Feature, ApprovalApiNames.Execute, command)).Response
                ?? ApprovalActionResult.Failure("Approval request failed.");
        }
    }
}
