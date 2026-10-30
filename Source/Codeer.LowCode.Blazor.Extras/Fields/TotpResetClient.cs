using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>サーバーが返す認証アプリの登録状態。</summary>
    public class TotpStatus
    {
        /// <summary>アプリで認証アプリの二要素認証が有効か (ログインアカウント契約に TOTP の列があるか)。</summary>
        public bool Enabled { get; set; }
        /// <summary>そのユーザーが登録済みか。</summary>
        public bool Registered { get; set; }
    }

    /// <summary>
    /// ログイン中の自分の認証アプリ (TOTP) の登録状態の取得と解除 (<see cref="MyTotpResetButtonField"/> が使う)。
    /// 対象は常に自分なのでサーバーはログイン中のユーザーで処理する。
    /// 既定は共通の口 (<see cref="ExtrasTransport"/> = ホストの 1 本の API の TotpApi)。専用のエンドポイントを持つホストは <see cref="StatusEndPoint"/> / <see cref="ResetEndPoint"/> を設定する (今までの使い方)。
    /// </summary>
    public static class TotpResetClient
    {
        /// <summary>状態取得の専用エンドポイント (GET {StatusEndPoint} → TotpStatus。例 "api/account/totp/status")。空 (既定) なら共通の口を使う。</summary>
        public static string StatusEndPoint { get; set; } = string.Empty;

        /// <summary>解除の専用エンドポイント (POST {ResetEndPoint} → TotpStatus。例 "api/account/totp/reset")。空 (既定) なら共通の口を使う。</summary>
        public static string ResetEndPoint { get; set; } = string.Empty;

        /// <summary>送る先があるか (専用のエンドポイント、または共通の口)。</summary>
        public static bool IsConfigured
            => (!string.IsNullOrEmpty(StatusEndPoint) && !string.IsNullOrEmpty(ResetEndPoint))
            || ExtrasTransport.Handler != null || !string.IsNullOrEmpty(ExtrasTransport.EndPoint);

        internal static async Task<TotpStatus?> GetStatusAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services services)
        {
            var http = services.Provider?.GetService<IHttpService>();
            if (!string.IsNullOrEmpty(StatusEndPoint))
            {
                if (http == null) return null;
                return await http.GetFromJsonAsync<TotpStatus>(StatusEndPoint, loading: false);
            }
            //画面を開くたびに走るので、失敗は通知せず null (旧ルートで未設定のときと同じ)
            return (await ExtrasTransport.CallAsync<TotpStatus>(http, TotpApiNames.Feature, TotpApiNames.Status, null, loading: false, onError: (_, _) => { })).Response;
        }

        /// <summary>確認ダイアログを出し、了承されたら解除する。解除後の状態を返す (キャンセル・失敗は null。失敗はトーストも出す)。</summary>
        internal static async Task<TotpStatus?> ConfirmAndResetAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services services, string? confirmMessage)
        {
            var message = string.IsNullOrEmpty(confirmMessage) ? Properties.Resources.TotpResetButton_ConfirmSelf : confirmMessage;
            var answer = await services.UIService.ShowMessageBox(string.Empty, message,
                [new DialogButton("btn btn-outline-danger", Properties.Resources.TotpResetButton_Action),
                 new DialogButton("btn btn-outline-secondary", Properties.Resources.Cancel)]);
            if (answer != Properties.Resources.TotpResetButton_Action) return null;

            var http = services.Provider?.GetService<IHttpService>();
            var status = !string.IsNullOrEmpty(ResetEndPoint)
                ? (http == null ? null : await http.PostAsJsonAsync<object, TotpStatus>(ResetEndPoint, new { }))
                : (await ExtrasTransport.CallAsync<TotpStatus>(http, TotpApiNames.Feature, TotpApiNames.Reset, null)).Response;
            if (status == null)
            {
                await services.UIService.NotifyError(Properties.Resources.TotpResetButton_Failed);
                return null;
            }
            await services.UIService.NotifySuccess(Properties.Resources.TotpResetButton_Done);
            return status;
        }
    }
}
