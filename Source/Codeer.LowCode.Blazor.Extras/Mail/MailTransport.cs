using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// メール系スクリプトオブジェクトの HTTP POST を置き換えるハンドラ。エンドポイントに
    /// POST されるものと同じ内容を受け取る。HTTP を介さないホスト (デスクトップアプリ =
    /// MailDispatcher 直呼び) が設定する。
    /// </summary>
    internal interface IMailTransportHandler
    {
        /// <summary>= POST SendMailEndPoint</summary>
        Task<MailSendResult> SendAsync(MailSendRequest request);

        /// <summary>= POST BulkSearchMailEndPoint</summary>
        Task<MailSendResult> SendBulkSearchAsync(MailBulkSearchRequest request);

        /// <summary>= POST PreviewMailEndPoint (プレビュー HTML を返す。未対応なら null)</summary>
        Task<byte[]?> PreviewAsync(MailPreviewRequest request) => Task.FromResult<byte[]?>(null);

        /// <summary>= POST BulkPreviewMailEndPoint (プレビュー HTML を返す。未対応なら null)</summary>
        Task<byte[]?> PreviewBulkSearchAsync(MailBulkSearchRequest request) => Task.FromResult<byte[]?>(null);
    }

    /// <summary>
    /// Mail スクリプトオブジェクトと BulkMailField の静的な送信経路の結線。
    /// Web アプリは起動時にエンドポイント URL を一度設定する (URL は Controller を持つアプリの持ち物)。
    /// デスクトップアプリは代わりに <see cref="Handler"/> を設定し、HTTP を介さず直接送る。
    /// 一斉送信は検索ベースの経路のみ (宛先はサーバーで解決し、アドレスはクライアントに渡らない)。
    /// </summary>
    public static class MailTransport
    {
        public static string SendMailEndPoint { get; set; } = string.Empty;
        public static string BulkSearchMailEndPoint { get; set; } = string.Empty;

        /// <summary>単発送信のプレビュー (HTML を返す)。空 = プレビュー機能なし。</summary>
        public static string PreviewMailEndPoint { get; set; } = string.Empty;

        /// <summary>一斉送信のプレビュー (HTML を返す)。空 = プレビュー機能なし。</summary>
        public static string BulkPreviewMailEndPoint { get; set; } = string.Empty;

        /// <summary>設定すると、全送信が HTTP エンドポイントの代わりにこのハンドラを通る。</summary>
        internal static IMailTransportHandler? Handler { get; set; }

        internal static async Task<MailSendResult> SendAsync(IHttpService? http, MailSendRequest request)
            => await PostAsync(http, SendMailEndPoint, request, static (handler, r) => handler.SendAsync(r));

        internal static async Task<MailSendResult> SendBulkSearchAsync(IHttpService? http, MailBulkSearchRequest request)
            => await PostAsync(http, BulkSearchMailEndPoint, request, static (handler, r) => handler.SendBulkSearchAsync(r));

        /// <summary>プレビュー HTML を取得する。エンドポイント未設定・失敗は null。</summary>
        internal static async Task<byte[]?> PreviewAsync(IHttpService? http, MailPreviewRequest request)
            => await PostForBytesAsync(http, PreviewMailEndPoint, request, static (handler, r) => handler.PreviewAsync(r));

        internal static async Task<byte[]?> PreviewBulkSearchAsync(IHttpService? http, MailBulkSearchRequest request)
            => await PostForBytesAsync(http, BulkPreviewMailEndPoint, request, static (handler, r) => handler.PreviewBulkSearchAsync(r));

        static async Task<byte[]?> PostForBytesAsync<TRequest>(IHttpService? http, string endPoint, TRequest request,
            Func<IMailTransportHandler, TRequest, Task<byte[]?>> byHandler)
        {
            if (Handler != null) return await byHandler(Handler, request);
            if (http == null || string.IsNullOrEmpty(endPoint)) return null;
            var response = await http.PostAsJsonReturnHttpResponseAsync(endPoint, request);
            if (response == null || !response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }

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
