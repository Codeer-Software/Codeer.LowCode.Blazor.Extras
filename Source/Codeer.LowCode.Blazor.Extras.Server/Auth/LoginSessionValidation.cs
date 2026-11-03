using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// ログイン済み Cookie の照合。Cookie に積んだセッションの印 (<see cref="LoginPrincipal.SessionStampClaimType"/>) と今のユーザー行の印・有効フラグを
    /// リクエストごとに突き合わせ、合わなければ Cookie を無効にする (保護された API は 401)。ホストは Cookie 認証の OnValidatePrincipal から呼ぶ:
    /// <code>
    /// options.Events.OnValidatePrincipal = async context =>
    /// {
    ///     var dataService = context.HttpContext.RequestServices.GetRequiredService&lt;DataService&gt;();
    ///     await LoginSessionValidation.ValidatePrincipalAsync(context, LoginAccountStore.Create(dataService.Design.DesignData, dataService.DbAccess));
    /// };
    /// </code>
    /// 契約に印の列 (DbColumnSessionStamp) が無ければ何もしない (従来どおり Cookie は有効期限まで使える)。
    /// 印の無い Cookie (印の列を足す前のログイン) は無効 = 列を足した時点で全員が再ログインになる。
    /// 照合の読みに失敗したとき (DB の障害) は例外のまま (500)。Cookie は消さないので、復旧後はそのまま使える。
    /// </summary>
    public static class LoginSessionValidation
    {
        /// <summary>Cookie の印を今のユーザー行と照合し、合わなければ Cookie を無効にする。accounts が null (契約無し) か印の列が無ければ何もしない。</summary>
        public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context, LoginAccountStore? accounts)
        {
            if (accounts == null || !accounts.HasSessionStamp) return;
            if (await accounts.IsSessionValidAsync(LoginPrincipal.GetUserId(context.Principal), LoginPrincipal.GetSessionStamp(context.Principal))) return;

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }
}
