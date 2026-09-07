using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 汎用 OpenID Connect (Keycloak / Auth0 / LINE 等) の設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "OidcLogins" の配列)。
    /// </summary>
    public class OidcLoginSettings
    {
        /// <summary>プロバイダ名 (URL・Cookie の idp クレーム・ボタンの識別)。英数字と - _ のみ。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>ログイン画面のボタンに出す名前。空なら <see cref="Name"/>。</summary>
        public string DisplayName { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        /// <summary>クライアントシークレット。appsettings.Development.json / 環境変数に置く (リポジトリには置かない)。</summary>
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>発行者 URL (discovery は {Authority}/.well-known/openid-configuration)。</summary>
        public string Authority { get; set; } = string.Empty;

        /// <summary>要求するスコープ。空なら openid email profile。</summary>
        public string[] Scopes { get; set; } = [];

        /// <summary>ユーザー名 (アプリのユーザーテーブルと照合する値) に使うクレーム名。空なら preferred_username → email → sub の順。</summary>
        public string LoginNameClaim { get; set; } = string.Empty;

        /// <summary>許可するメールドメイン (example.co.jp 等)。空なら制限しない。ユーザー名の @ 以降で判定する。</summary>
        public string[] AllowedDomains { get; set; } = [];

        /// <summary>ClientId が設定されていれば有効。</summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
    }

    /// <summary>
    /// 汎用 OpenID Connect プロバイダ。Entra / Google / Cognito はこれを継承して癖の部分だけを上書きしている。
    /// 独自 IdP で既定と違うところがあれば同じように継承して <see cref="Configure"/> / <see cref="CreateIdentity"/> / ログアウトを上書きする。
    /// </summary>
    public class OidcLoginProvider : IExternalLoginProvider
    {
        readonly string _clientId;
        readonly string _clientSecret;
        readonly string _authority;
        readonly string[] _scopes;
        readonly string _loginNameClaim;
        readonly string[] _allowedDomains;

        public OidcLoginProvider(OidcLoginSettings settings)
            : this(settings.Name, settings.DisplayName, settings.ClientId, settings.ClientSecret, settings.Authority, settings.Scopes, settings.LoginNameClaim, settings.AllowedDomains) { }

        protected OidcLoginProvider(string name, string displayName, string clientId, string clientSecret, string authority, string[] scopes, string loginNameClaim, string[] allowedDomains)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("External login: Name is required.");
            if (string.IsNullOrWhiteSpace(authority)) throw new InvalidOperationException($"External login '{name}': Authority is required.");
            Name = name.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _authority = authority.Trim();
            //openid が無いと OIDC として成立しない (id_token が返らない) ので、明示された Scopes に無くても必ず足す
            _scopes = scopes.Length == 0 ? ["openid", "email", "profile"]
                : scopes.Any(s => s == "openid") ? scopes : ["openid", .. scopes];
            _loginNameClaim = loginNameClaim.Trim();
            _allowedDomains = allowedDomains;
        }

        public string Name { get; }
        public string DisplayName { get; }

        public virtual void Configure(OpenIdConnectOptions options)
        {
            options.Authority = _authority;
            options.ClientId = _clientId;
            options.ClientSecret = _clientSecret;
            options.Scope.Clear();
            foreach (var s in _scopes) options.Scope.Add(s);
        }

        public virtual ExternalLoginIdentity? CreateIdentity(ClaimsPrincipal principal, out string error)
        {
            error = string.Empty;
            var subject = principal.FindFirst("sub")?.Value;
            if (string.IsNullOrEmpty(subject)) { error = ExternalLoginError.InvalidClaims; return null; }

            var loginName = GetLoginName(principal);
            if (string.IsNullOrEmpty(loginName)) { error = ExternalLoginError.InvalidClaims; return null; }

            //メールをユーザー名にするときは、IdP が「未検証」と言っているメールを信じない (OIDC Core 5.1: email_verified)。
            //クレームを返さない IdP は判定できないので通す (Google / Cognito は各クラスで「true が必須」まで要求する)
            if (LoginNameComesFromEmail(principal) && string.Equals(principal.FindFirst("email_verified")?.Value, "false", StringComparison.OrdinalIgnoreCase))
            {
                error = ExternalLoginError.InvalidClaims;
                return null;
            }

            if (!IsDomainAllowed(loginName)) { error = ExternalLoginError.DomainNotAllowed; return null; }

            return new ExternalLoginIdentity
            {
                Provider = Name,
                Subject = subject,
                LoginName = loginName,
                Email = principal.FindFirst("email")?.Value,
                DisplayName = principal.FindFirst("name")?.Value,
                Issuer = principal.FindFirst("iss")?.Value,
                Claims = principal,
            };
        }

        /// <summary>ユーザー名にするクレーム値。設定の LoginNameClaim があればそれ、無ければ preferred_username → email → sub。</summary>
        protected virtual string? GetLoginName(ClaimsPrincipal principal)
            => string.IsNullOrEmpty(_loginNameClaim)
                ? principal.FindFirst("preferred_username")?.Value ?? principal.FindFirst("email")?.Value ?? principal.FindFirst("sub")?.Value
                : principal.FindFirst(_loginNameClaim)?.Value;

        /// <summary>設定の LoginNameClaim (未設定なら空)。</summary>
        protected string LoginNameClaim => _loginNameClaim;

        /// <summary>ユーザー名が email クレームから来るか (LoginNameClaim が email、または既定順で preferred_username が無く email がある)。</summary>
        protected virtual bool LoginNameComesFromEmail(ClaimsPrincipal principal)
            => string.IsNullOrEmpty(_loginNameClaim)
                ? principal.FindFirst("preferred_username") == null && principal.FindFirst("email") != null
                : _loginNameClaim == "email";

        /// <summary>email_verified クレームが true か。自己申告のメールを持てる IdP (Google / Cognito) はメールをユーザー名にする前にこれを見る。</summary>
        protected static bool IsEmailVerified(ClaimsPrincipal principal)
            => string.Equals(principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        protected bool IsDomainAllowed(string loginName)
        {
            if (_allowedDomains.Length == 0) return true;
            var at = loginName.LastIndexOf('@');
            var domain = at < 0 ? string.Empty : loginName.Substring(at + 1);
            return _allowedDomains.Any(d => d.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>既定: discovery に end_session_endpoint があれば IdP ログアウトも行う。</summary>
        public virtual async Task<bool> HasIdpSignOutAsync(OpenIdConnectOptions options)
        {
            try
            {
                if (options.ConfigurationManager == null) return false;
                var config = await options.ConfigurationManager.GetConfigurationAsync(CancellationToken.None);
                return !string.IsNullOrEmpty(config.EndSessionEndpoint);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>既定: Cookie 破棄 + OIDC の end-session (id_token_hint は保持したトークンをハンドラが載せる)。IdP は /signout-{name} に戻し、そこから redirectUri へ。</summary>
        public virtual Task<IActionResult> SignOutAsync(ControllerBase controller, string scheme, string redirectUri)
            => Task.FromResult<IActionResult>(controller.SignOut(new AuthenticationProperties { RedirectUri = redirectUri },
                CookieAuthenticationDefaults.AuthenticationScheme, scheme));
    }
}
