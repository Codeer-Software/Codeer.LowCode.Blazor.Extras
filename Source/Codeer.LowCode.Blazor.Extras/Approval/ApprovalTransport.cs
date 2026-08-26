using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// ApprovalFlowField の command API 呼び出し。エンドポイントのベース URL はアプリ (Controller を持つ側) が起動時に設定する。
    /// </summary>
    public static class ApprovalTransport
    {
        /// <summary>command API の URL (例: "/api/approval")。すべての操作をこの 1 本に POST する。</summary>
        public static string EndPointBase { get; set; } = string.Empty;

        internal static async Task<ApprovalActionResult> ExecuteAsync(IHttpService? http, ApprovalCommand command)
        {
            if (http == null || string.IsNullOrEmpty(EndPointBase))
                return ApprovalActionResult.Failure("Approval endpoint is not configured.");
            return await http.PostAsJsonAsync<ApprovalCommand, ApprovalActionResult>(EndPointBase, command)
                ?? ApprovalActionResult.Failure("Approval request failed.");
        }
    }
}
