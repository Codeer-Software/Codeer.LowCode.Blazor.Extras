using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>AWS Cognito ユーザープールの設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "CognitoLogin")。</summary>
    public class CognitoLoginSettings
    {
        /// <summary>ログイン画面のボタンに出す名前。空なら "Cognito"。</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>アプリクライアント ID。</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>アプリクライアントのシークレット (無しのクライアントなら空)。appsettings.Development.json / 環境変数 (CognitoLogin__ClientSecret) に置く。</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>発行者 URL: https://cognito-idp.{region}.amazonaws.com/{userPoolId}</summary>
        public string Authority { get; set; } = string.Empty;

        /// <summary>ホスト UI ドメイン (https://xxx.auth.{region}.amazoncognito.com)。ログアウト時に IdP 側のセッションも終わらせるときに設定。空ならローカルログアウトのみ。</summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>ユーザー名に使うクレーム。空なら email (検証済みのみ)。メール検証をオフにしているプールは "cognito:username" にする。</summary>
        public string LoginNameClaim { get; set; } = string.Empty;

        /// <summary>許可するメールドメイン。空なら制限しない。</summary>
        public string[] AllowedDomains { get; set; } = [];

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>
    /// AWS Cognito。ユーザー名は既定で検証済みメール。discovery に end_session_endpoint が無いので、
    /// ログアウトはホスト UI の /logout (logout_uri はアプリクライアントの「許可されているサインアウト URL」に登録)。
    /// </summary>
    public class CognitoLoginProvider : OidcLoginProvider
    {
        public const string DefaultName = "Cognito";

        readonly string _clientId;
        readonly string _domain;

        public CognitoLoginProvider(CognitoLoginSettings settings, string name = DefaultName)
            : base(name, settings.DisplayName, settings.ClientId, settings.ClientSecret, settings.Authority, [], settings.LoginNameClaim, settings.AllowedDomains)
        {
            _clientId = settings.ClientId;
            _domain = settings.Domain.Trim().TrimEnd('/');
        }

        protected override string? GetLoginName(ClaimsPrincipal principal)
            => string.IsNullOrEmpty(LoginNameClaim) ? principal.FindFirst("email")?.Value : base.GetLoginName(principal);

        public override ExternalLoginIdentity? CreateIdentity(ClaimsPrincipal principal, out string error)
        {
            //メールをユーザー名にするときは検証済みのものだけ
            if (string.IsNullOrEmpty(LoginNameClaim) && !IsEmailVerified(principal))
            {
                error = ExternalLoginError.InvalidClaims;
                return null;
            }
            return base.CreateIdentity(principal, out error);
        }

        public override Task<bool> HasIdpSignOutAsync(OpenIdConnectOptions options) => Task.FromResult(_domain.Length > 0);

        public override async Task<IActionResult> SignOutAsync(ControllerBase controller, string scheme, string redirectUri)
        {
            await controller.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var request = controller.Request;
            var logoutUri = $"{request.Scheme}://{request.Host}{redirectUri}";
            return controller.Redirect($"{_domain}/logout?client_id={Uri.EscapeDataString(_clientId)}&logout_uri={Uri.EscapeDataString(logoutUri)}");
        }
    }
}
