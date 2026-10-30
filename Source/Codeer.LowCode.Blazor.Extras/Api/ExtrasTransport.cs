using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Api
{
    /// <summary>
    /// Extras の機能がホストの 1 本の API へ要求を送る共通の口。
    /// Web アプリは <see cref="EndPoint"/> (既定 "/api/extras" = テンプレの Controllers/ExtrasController.cs) へ POST する。
    /// デスクトップアプリなど HTTP を使わないホストは <see cref="Handler"/> を設定し、Extras.Server の ExtrasApi を直接呼ぶ。
    /// 機能ごとの古い結線 (MailTransport の EndPoint / Handler、JobTriggerField の EndPoint / RunCoreAsync) が設定されていれば、その機能はそちらを使う。
    /// </summary>
    public static class ExtrasTransport
    {
        public static string EndPoint { get; set; } = "/api/extras";

        /// <summary>設定すると、全機能の要求が HTTP の代わりにこのハンドラを通る (デスクトップアプリ = Extras.Server の ExtrasApi.ExecuteAsync を呼ぶ実装をアプリ側で用意する)。</summary>
        public static Func<ExtrasRequest, Task<ExtrasResponse?>>? Handler { get; set; }

        /// <summary>送る先があるか (Handler、または EndPoint と http)。無いときは呼び出し側が「未設定」として扱う。</summary>
        public static bool CanSend(IHttpService? http) => Handler != null || (http != null && !string.IsNullOrEmpty(EndPoint));

        /// <summary>
        /// 要求を送って応答を読む。Delivered = 届いて応答が返った (応答が無い操作は Response が null)。
        /// 届かなかった (送る先が無い・HTTP の失敗) ときは Delivered = false。HTTP の失敗は HttpService が通知する (onError を渡すと通知せず状態コードと文言をそこへ = 状態の問い合わせの繰り返しなど)。
        /// </summary>
        internal static async Task<(TResponse? Response, bool Delivered)> CallAsync<TResponse>(IHttpService? http, string feature, string action, object? request, bool loading = true, Action<HttpStatusCode, string>? onError = null)
            where TResponse : class
        {
            var envelope = new ExtrasRequest
            {
                Feature = feature,
                Action = action,
                Payload = request == null ? string.Empty : JsonConverterEx.SerializeObject(request, indented: false),
            };
            ExtrasResponse? response;
            if (Handler != null)
            {
                //HTTP と同じ扱い: onError があれば失敗をそこへ渡して「届かなかった」、無ければ呼び出し側へ伝える
                if (onError == null) response = await Handler(envelope);
                else
                {
                    try { response = await Handler(envelope); }
                    catch (Exception e)
                    {
                        onError(HttpStatusCode.InternalServerError, e.Message);
                        return (null, false);
                    }
                }
            }
            else if (!CanSend(http)) return (null, false);
            else
            {
                using var checker = onError == null ? null : http!.AddChecker(onError);
                response = await http!.PostAsJsonAsync<ExtrasRequest, ExtrasResponse>(EndPoint, envelope, loading);
            }
            if (response == null) return (null, false);
            return (string.IsNullOrEmpty(response.Payload) ? null : JsonConverterEx.DeserializeObject<TResponse>(response.Payload), true);
        }
    }
}
