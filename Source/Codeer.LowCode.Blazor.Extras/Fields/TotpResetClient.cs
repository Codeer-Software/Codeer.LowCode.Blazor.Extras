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
    /// 対象は常に自分なのでサーバーはログイン中のユーザーで処理する。エンドポイントはホストが結線する (テンプレートは ServiceInitializer)。
    /// </summary>
    public static class TotpResetClient
    {
        /// <summary>状態取得 (GET {StatusEndPoint} → TotpStatus)。テンプレートは "api/account/totp/status"。</summary>
        public static string StatusEndPoint { get; set; } = string.Empty;

        /// <summary>解除 (POST {ResetEndPoint} → TotpStatus)。テンプレートは "api/account/totp/reset"。</summary>
        public static string ResetEndPoint { get; set; } = string.Empty;

        public static bool IsConfigured => !string.IsNullOrEmpty(StatusEndPoint) && !string.IsNullOrEmpty(ResetEndPoint);

        internal static async Task<TotpStatus?> GetStatusAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services services)
        {
            var http = services.Provider?.GetService<IHttpService>();
            if (http == null || string.IsNullOrEmpty(StatusEndPoint)) return null;
            return await http.GetFromJsonAsync<TotpStatus>(StatusEndPoint, loading: false);
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
            var status = http == null || string.IsNullOrEmpty(ResetEndPoint)
                ? null
                : await http.PostAsJsonAsync<object, TotpStatus>(ResetEndPoint, new { });
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
