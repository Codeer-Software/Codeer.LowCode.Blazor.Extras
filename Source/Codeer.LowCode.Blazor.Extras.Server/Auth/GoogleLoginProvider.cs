using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>Google の設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "GoogleLogin")。</summary>
    public class GoogleLoginSettings
    {
        /// <summary>ログイン画面のボタンに出す名前。空なら "Google"。</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>OAuth クライアント ID (ウェブ アプリケーション)。</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>クライアントシークレット。appsettings.Development.json / 環境変数 (GoogleLogin__ClientSecret) に置く。</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>許可するメールドメイン (Google Workspace のドメイン等)。空なら制限しない。</summary>
        public string[] AllowedDomains { get; set; } = [];

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>Google。ユーザー名は検証済みメール (email_verified == true のみ)。end-session が無いのでログアウトはローカルのみ。</summary>
    public class GoogleLoginProvider : OidcLoginProvider
    {
        public const string DefaultName = "Google";

        public GoogleLoginProvider(GoogleLoginSettings settings, string name = DefaultName)
            : base(name, settings.DisplayName, settings.ClientId, settings.ClientSecret, "https://accounts.google.com", [], "email", settings.AllowedDomains) { }

        public override ExternalLoginIdentity? CreateIdentity(ClaimsPrincipal principal, out string error)
        {
            if (!IsEmailVerified(principal))
            {
                error = ExternalLoginError.InvalidClaims;
                return null;
            }
            return base.CreateIdentity(principal, out error);
        }

        public override Task<bool> HasIdpSignOutAsync(OpenIdConnectOptions options) => Task.FromResult(false);
    }
}
