using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 認証 Cookie に積むユーザーの形。ログインの経路 (パスワード / 外部 IdP / ネイティブアプリのチケット交換) はすべてここで作り、
    /// ユーザー Id を読む側 (アプリの IAuthenticationContext・監査ログ・IdP ログアウトの判定) もここで読む。
    /// 積むのはユーザー Id (ユーザーモジュールの行の Id)・表示名・外部 IdP の経路名・セッションの印 (契約に印の列があるとき) だけ。
    /// </summary>
    public static class LoginPrincipal
    {
        /// <summary>外部 IdP でサインインしたセッションに付く「どのプロバイダか」のクレーム名。パスワードログインには付けない。</summary>
        public const string IdpClaimType = "idp";

        /// <summary>
        /// セッションの印のクレーム名。ログイン時のユーザー行の印 (LoginAccountContractField の DbColumnSessionStamp) をそのまま積む。
        /// リクエストごとに今の行の印と照合し (<see cref="LoginSessionValidation"/>)、違えば Cookie を無効にする。
        /// </summary>
        public const string SessionStampClaimType = "session_stamp";

        /// <summary>
        /// サインインするプリンシパル。provider は外部 IdP の経路名 (パスワードログインは null)。
        /// sessionStamp はログインで読んだユーザー行の印 (<see cref="LoginAccount.SessionStamp"/>。契約に印の列が無ければ null)。
        /// </summary>
        public static ClaimsPrincipal Create(string userId, string displayName, string? provider = null, string? sessionStamp = null)
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
            if (!string.IsNullOrEmpty(provider)) identity.AddClaim(new Claim(IdpClaimType, provider));
            if (!string.IsNullOrEmpty(sessionStamp)) identity.AddClaim(new Claim(SessionStampClaimType, sessionStamp));
            return new ClaimsPrincipal(identity);
        }

        /// <summary>ユーザー Id。未認証なら空文字。</summary>
        public static string GetUserId(ClaimsPrincipal? user)
            => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        /// <summary>表示名。未認証なら空文字。</summary>
        public static string GetDisplayName(ClaimsPrincipal? user)
            => user?.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

        /// <summary>外部 IdP の経路名。パスワードログイン・未認証なら null。</summary>
        public static string? GetProvider(ClaimsPrincipal? user)
            => user?.FindFirst(IdpClaimType)?.Value;

        /// <summary>セッションの印。印の無い Cookie (契約に印の列が無いときのログイン・印を使う前の旧 Cookie)・未認証なら null。</summary>
        public static string? GetSessionStamp(ClaimsPrincipal? user)
            => user?.FindFirst(SessionStampClaimType)?.Value;
    }
}
