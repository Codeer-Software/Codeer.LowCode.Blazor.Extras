using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>Microsoft Entra ID (旧 Azure AD) の設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "EntraLogin")。</summary>
    public class EntraLoginSettings
    {
        /// <summary>ログイン画面のボタンに出す名前。空なら "Entra"。</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>アプリ登録のアプリケーション (クライアント) ID。</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>クライアントシークレット。最長 24 か月で失効するので更新手順を運用に組み込む。appsettings.Development.json / 環境変数 (EntraLogin__ClientSecret) に置く。</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>GUID = そのテナントの職場アカウントだけ / "organizations" (既定) = 任意の職場アカウント / "common" = 個人アカウントも。</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>ゲストユーザー (#EXT#) を許可する。既定は拒否。</summary>
        public bool AllowGuests { get; set; }

        /// <summary>許可する UPN のドメイン。空なら制限しない。</summary>
        public string[] AllowedDomains { get; set; } = [];

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>
    /// Entra ID。ユーザー名は UPN (preferred_username。email クレームは詐称できる)。ゲストは既定で拒否。
    /// organizations / common は issuer がテナントごとに違うので形式で検証する。ログアウトは end-session (post_logout_redirect_uri = /signout-entra)。
    /// </summary>
    public class EntraLoginProvider : OidcLoginProvider
    {
        public const string DefaultName = "Entra";
        const string Organizations = "organizations";
        static readonly Regex IssuerPattern = new("^https://login\\.microsoftonline\\.com/[0-9a-fA-F-]{36}/v2\\.0$", RegexOptions.Compiled);

        readonly bool _multiTenant;
        readonly bool _allowGuests;

        public EntraLoginProvider(EntraLoginSettings settings, string name = DefaultName)
            : base(name, settings.DisplayName, settings.ClientId, settings.ClientSecret,
                $"https://login.microsoftonline.com/{Tenant(settings.TenantId)}/v2.0", [], "preferred_username", settings.AllowedDomains)
        {
            var tenant = Tenant(settings.TenantId);
            _multiTenant = tenant.Equals(Organizations, StringComparison.OrdinalIgnoreCase) || tenant.Equals("common", StringComparison.OrdinalIgnoreCase);
            _allowGuests = settings.AllowGuests;
        }

        static string Tenant(string tenantId) => string.IsNullOrWhiteSpace(tenantId) ? Organizations : tenantId.Trim();

        public override void Configure(OpenIdConnectOptions options)
        {
            base.Configure(options);
            if (_multiTenant) options.TokenValidationParameters.IssuerValidator = ValidateIssuer;
        }

        // 許可する issuer: https://login.microsoftonline.com/{テナントID(GUID)}/v2.0
        internal static string ValidateIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
        {
            if (IssuerPattern.IsMatch(issuer)) return issuer;
            throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}");
        }

        public override ExternalLoginIdentity? CreateIdentity(ClaimsPrincipal principal, out string error)
        {
            var identity = base.CreateIdentity(principal, out error);
            if (identity == null) return null;
            if (!_allowGuests && identity.LoginName.Contains("#EXT#", StringComparison.OrdinalIgnoreCase))
            {
                error = ExternalLoginError.GuestNotAllowed;
                return null;
            }
            return identity;
        }

        public override Task<bool> HasIdpSignOutAsync(OpenIdConnectOptions options) => Task.FromResult(true);
    }
}
