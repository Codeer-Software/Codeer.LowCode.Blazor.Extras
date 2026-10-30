using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// メール系スクリプトオブジェクトの HTTP POST を置き換えるハンドラ。エンドポイントに
    /// POST されるものと同じ内容を受け取る。HTTP を介さないホスト (デスクトップアプリ =
    /// MailDispatcher 直呼び) が設定する。
    /// </summary>
    public interface IMailTransportHandler
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
    /// 何も設定しなければ共通の口 (<see cref="ExtrasTransport"/> = ホストの 1 本の API の MailApi) を使う。
    /// メール専用のルートを持つアプリ (自分の MailController) は起動時に <see cref="SendMailEndPoint"/> 等を設定する。
    /// デスクトップアプリは <see cref="Handler"/> (または ExtrasTransport.Handler) を設定し、HTTP を介さず直接送る。
    /// 一斉送信は検索ベースの経路のみ (宛先はサーバーで解決し、アドレスはクライアントに渡らない)。
    /// </summary>
    public static class MailTransport
    {
        /// <summary>メール専用のルートの URL。設定するとメールの全操作がこちら (専用ルート) になる。空 = 共通の口。</summary>
        public static string SendMailEndPoint { get; set; } = string.Empty;
        public static string BulkSearchMailEndPoint { get; set; } = string.Empty;

        /// <summary>単発送信のプレビュー (HTML を返す)。専用ルートで空 = プレビュー機能なし。</summary>
        public static string PreviewMailEndPoint { get; set; } = string.Empty;

        /// <summary>一斉送信のプレビュー (HTML を返す)。専用ルートで空 = プレビュー機能なし。</summary>
        public static string BulkPreviewMailEndPoint { get; set; } = string.Empty;

        /// <summary>
        /// 設定すると、全送信・プレビューが HTTP エンドポイントの代わりにこのハンドラを通る
        /// (デスクトップアプリ = MailDispatcher / MailBulkSearch / MailPreviewBuilder を直接呼ぶ実装をアプリ側で用意する)。
        /// </summary>
        public static IMailTransportHandler? Handler { get; set; }

        //メール専用の結線 (Handler か SendMailEndPoint) があればそちら、無ければ共通の口
        static bool UsesOwnRoute => Handler != null || !string.IsNullOrEmpty(SendMailEndPoint);

        internal static async Task<MailSendResult> SendAsync(IHttpService? http, MailSendRequest request)
            => UsesOwnRoute
                ? await PostAsync(http, SendMailEndPoint, request, static (handler, r) => handler.SendAsync(r))
                : await CallAsync(http, MailApiNames.Send, request);

        internal static async Task<MailSendResult> SendBulkSearchAsync(IHttpService? http, MailBulkSearchRequest request)
            => UsesOwnRoute
                ? await PostAsync(http, BulkSearchMailEndPoint, request, static (handler, r) => handler.SendBulkSearchAsync(r))
                : await CallAsync(http, MailApiNames.SendBulkSearch, request);

        /// <summary>プレビュー HTML を取得する。エンドポイント未設定・失敗は null。</summary>
        internal static async Task<byte[]?> PreviewAsync(IHttpService? http, MailPreviewRequest request)
            => UsesOwnRoute
                ? await PostForBytesAsync(http, PreviewMailEndPoint, request, static (handler, r) => handler.PreviewAsync(r))
                : await CallForBytesAsync(http, MailApiNames.Preview, request);

        internal static async Task<byte[]?> PreviewBulkSearchAsync(IHttpService? http, MailBulkSearchRequest request)
            => UsesOwnRoute
                ? await PostForBytesAsync(http, BulkPreviewMailEndPoint, request, static (handler, r) => handler.PreviewBulkSearchAsync(r))
                : await CallForBytesAsync(http, MailApiNames.PreviewBulkSearch, request);

        static async Task<MailSendResult> CallAsync<TRequest>(IHttpService? http, string action, TRequest request)
        {
            if (!ExtrasTransport.CanSend(http)) return MailSendResult.Failure(string.Empty, "Mail endpoint is not configured.");
            var (result, _) = await ExtrasTransport.CallAsync<MailSendResult>(http, MailApiNames.Feature, action, request);
            return result ?? MailSendResult.Failure(string.Empty, "Mail request failed.");
        }

        static async Task<byte[]?> CallForBytesAsync<TRequest>(IHttpService? http, string action, TRequest request)
        {
            //失敗は null で返して UI 側が扱う (旧ルートと同じく通知しない)
            var (html, _) = await ExtrasTransport.CallAsync<string>(http, MailApiNames.Feature, action, request, onError: (_, _) => { });
            return html == null ? null : System.Text.Encoding.UTF8.GetBytes(html);
        }

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
