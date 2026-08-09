using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// Replaces the HTTP POST of the mail script objects. Receives exactly what would be posted
    /// to the endpoints. Set by hosts that do not go through HTTP (desktop apps send directly
    /// via MailDispatcher).
    /// </summary>
    public interface IMailTransportHandler
    {
        /// <summary>= POST SendMailEndPoint</summary>
        Task<MailSendResult> SendAsync(MailSendRequest request);

        /// <summary>= POST BulkSearchMailEndPoint</summary>
        Task<MailSendResult> SendBulkSearchAsync(MailBulkSearchRequest request);
    }

    /// <summary>
    /// Static transport wiring for the Mail script object and the BulkMailField.
    /// Web apps set the endpoint URLs once at startup (URLs belong to the app, which owns the
    /// controllers). Desktop apps set <see cref="Handler"/> instead and send directly without HTTP.
    /// Bulk sends go through the search-based path only (recipients are resolved on the server,
    /// addresses never travel to the client).
    /// </summary>
    public static class MailTransport
    {
        public static string SendMailEndPoint { get; set; } = string.Empty;
        public static string BulkSearchMailEndPoint { get; set; } = string.Empty;

        /// <summary>When set, all sends go through this handler instead of the HTTP endpoints.</summary>
        public static IMailTransportHandler? Handler { get; set; }

        internal static async Task<MailSendResult> SendAsync(IHttpService? http, MailSendRequest request)
            => await PostAsync(http, SendMailEndPoint, request, static (handler, r) => handler.SendAsync(r));

        internal static async Task<MailSendResult> SendBulkSearchAsync(IHttpService? http, MailBulkSearchRequest request)
            => await PostAsync(http, BulkSearchMailEndPoint, request, static (handler, r) => handler.SendBulkSearchAsync(r));

        static async Task<MailSendResult> PostAsync<TRequest>(IHttpService? http, string endPoint, TRequest request,
            Func<IMailTransportHandler, TRequest, Task<MailSendResult>> sendByHandler)
        {
            if (Handler != null) return await sendByHandler(Handler, request);
            if (http == null || string.IsNullOrEmpty(endPoint))
                return MailSendResult.Failure(string.Empty, "Mail endpoint is not configured.");
            return await http.PostAsJsonAsync<TRequest, MailSendResult>(endPoint, request)
                ?? MailSendResult.Failure(string.Empty, "Mail request failed.");
        }
    }
}
