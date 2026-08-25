using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// ApprovalFlowField の command API 呼び出し。エンドポイントのベース URL はアプリ (Controller を持つ側) が起動時に設定する。
    /// </summary>
    public static class ApprovalTransport
    {
        /// <summary>command API のベース URL (例: "/api/approval")。</summary>
        public static string EndPointBase { get; set; } = string.Empty;

        internal static async Task<ApprovalActionResult> SubmitAsync(IHttpService? http, ApprovalSubmitRequest request)
        {
            var action = string.IsNullOrEmpty(request.FlowId) ? "submit" : "resubmit";
            return await PostAsync(http, action, request);
        }

        internal static async Task<ApprovalActionResult> ExecuteAsync(IHttpService? http, string action, ApprovalActionRequest request)
        {
            return await PostAsync(http, action.ToLowerInvariant(), request);
        }

        static async Task<ApprovalActionResult> PostAsync<TRequest>(IHttpService? http, string action, TRequest request)
        {
            if (http == null || string.IsNullOrEmpty(EndPointBase))
                return ApprovalActionResult.Failure("Approval endpoint is not configured.");
            return await http.PostAsJsonAsync<TRequest, ApprovalActionResult>($"{EndPointBase}/{action}", request)
                ?? ApprovalActionResult.Failure("Approval request failed.");
        }
    }
}
