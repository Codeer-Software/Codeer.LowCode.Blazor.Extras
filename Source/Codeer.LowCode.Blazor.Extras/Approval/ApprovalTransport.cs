using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// Replaces the HTTP POST of the approval command API. Receives exactly what would be posted
    /// to the endpoints. Set by hosts that do not go through HTTP (desktop apps call the
    /// ApprovalEngine directly).
    /// </summary>
    public interface IApprovalTransportHandler
    {
        /// <summary>= POST {EndPointBase}/submit または /resubmit (FlowId の有無で区別)。</summary>
        Task<ApprovalActionResult> SubmitAsync(ApprovalSubmitRequest request);

        /// <summary>= POST {EndPointBase}/{action} (approve / reject / return / withdraw / cancel / confirm)。</summary>
        Task<ApprovalActionResult> ExecuteAsync(string action, ApprovalActionRequest request);
    }

    /// <summary>
    /// Static transport wiring for the ApprovalFlowField. Web apps set the endpoint base once at
    /// startup (URLs belong to the app, which owns the controllers). Desktop apps set
    /// <see cref="Handler"/> instead and call the engine directly without HTTP.
    /// </summary>
    public static class ApprovalTransport
    {
        /// <summary>command API のベース URL (例: "/api/approval")。</summary>
        public static string EndPointBase { get; set; } = string.Empty;

        /// <summary>When set, all requests go through this handler instead of the HTTP endpoints.</summary>
        public static IApprovalTransportHandler? Handler { get; set; }

        internal static async Task<ApprovalActionResult> SubmitAsync(IHttpService? http, ApprovalSubmitRequest request)
        {
            if (Handler != null) return await Handler.SubmitAsync(request);
            var action = string.IsNullOrEmpty(request.FlowId) ? "submit" : "resubmit";
            return await PostAsync(http, action, request);
        }

        internal static async Task<ApprovalActionResult> ExecuteAsync(IHttpService? http, string action, ApprovalActionRequest request)
        {
            if (Handler != null) return await Handler.ExecuteAsync(action, request);
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
