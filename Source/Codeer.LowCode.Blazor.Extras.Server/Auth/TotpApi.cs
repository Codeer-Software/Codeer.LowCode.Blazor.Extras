using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 共通の API で受ける、ログイン中の自分の認証アプリ (TOTP) の登録状態と解除 (MyTotpResetButtonField が使う)。中身は <see cref="TotpLogin"/>。
    /// 対象は常に現在のユーザー (ExtrasApiContext.UserId)。列は書き込み専用なのでここが唯一の入口。
    /// </summary>
    public sealed class TotpApi : IExtrasApiHandler
    {
        readonly TotpLoginSettings _settings;

        /// <param name="settings">appsettings の TotpLogin。</param>
        public TotpApi(TotpLoginSettings settings) => _settings = settings;

        public string Feature => TotpApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            TotpApiNames.Reset => AuditCategory.Authentication,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            //対象のユーザーの鍵。空のまま動かすと誰の登録でもない行を読み書きする
            if (string.IsNullOrEmpty(context.UserId)) throw new InvalidOperationException("ExtrasApiContext.UserId is required for TOTP.");
            var totp = TotpLogin.Create(context.DesignData, _settings, context.DbAccess);
            switch (action)
            {
                case TotpApiNames.Status:
                {
                    if (totp == null) return new TotpStatus();
                    var current = await totp.FindAsync(context.UserId);
                    return new TotpStatus { Enabled = true, Registered = current?.IsConfirmed == true };
                }

                //アプリで TOTP が無効なら応答なし
                case TotpApiNames.Reset:
                    if (totp == null) return null;
                    await totp.ResetAsync(context.UserId);
                    return new TotpStatus { Enabled = true, Registered = false };

                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }
    }
}
