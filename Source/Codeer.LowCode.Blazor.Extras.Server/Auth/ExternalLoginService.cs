using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary><see cref="ExternalLoginAuthentication.AddExternalLogins"/> の追加設定。</summary>
    public class ExternalLoginOptions
    {
        /// <summary>失敗時に差し戻すログイン画面。?error=理由 を付けて遷移する。</summary>
        public string LoginPath { get; set; } = "/login.html";

        /// <summary>
        /// ネイティブアプリ (MAUI) がシステムブラウザでログインした後に戻る URL (lowcodeapp://auth 等)。
        /// 空ならモバイルのチャレンジ (?mobile=true) は受け付けない。
        /// </summary>
        public string MobileCallbackUrl { get; set; } = string.Empty;

        /// <summary>モバイル用の使い捨てチケットの有効期間。</summary>
        public TimeSpan MobileTicketLifetime { get; set; } = TimeSpan.FromMinutes(2);
    }

    /// <summary>ログイン画面に出す選択肢 1 つ。</summary>
    public record ExternalLoginOption(string Name, string DisplayName);

    /// <summary>
    /// 登録された <see cref="IExternalLoginProvider"/> の一覧と、チャレンジ / ログアウト / モバイル用チケットの処理。
    /// エンドポイント (ルーティング) はアプリの AccountController が持ち、ここはその中身を提供する。
    /// </summary>
    public class ExternalLoginService
    {
        /// <summary>Cookie に積む「このセッションはどのプロバイダでサインインしたか」のクレーム名。パスワードログインには付けない。</summary>
        public const string IdpClaimType = "idp";

        internal const string MobileItem = "codeer.mobile";
        const string TicketKeyPrefix = "codeer.extlogin.ticket.";

        readonly Dictionary<string, IExternalLoginProvider> _providers;
        readonly ExternalLoginOptions _options;
        readonly IDistributedCache _cache;
        readonly IOptionsMonitor<OpenIdConnectOptions> _oidcOptions;

        internal ExternalLoginService(IEnumerable<IExternalLoginProvider> providers, ExternalLoginOptions options, IDistributedCache cache, IOptionsMonitor<OpenIdConnectOptions> oidcOptions)
        {
            _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            _options = options;
            _cache = cache;
            _oidcOptions = oidcOptions;
            Options = _providers.Values.Select(p => new ExternalLoginOption(p.Name, p.DisplayName)).ToList();
        }

        /// <summary>有効なプロバイダ (ログイン画面のボタン)。</summary>
        public IReadOnlyList<ExternalLoginOption> Options { get; }

        public bool IsMobileEnabled => !string.IsNullOrWhiteSpace(_options.MobileCallbackUrl);

        /// <summary>プロバイダの認証スキーム名 (AddOpenIdConnect の登録名)。</summary>
        public static string SchemeOf(string providerName) => $"ExternalLogin.{providerName}";

        internal IExternalLoginProvider? Find(string? provider)
            => provider != null && _providers.TryGetValue(provider, out var p) ? p : null;

        /// <summary>
        /// 外部 IdP へのチャレンジ。ブラウザ遷移 (GET) から呼ぶ。
        /// persistent = true はブラウザを閉じても残る Cookie にする (ログイン画面の「ログイン状態を保持する」。パスワードログインの IsPersistent と同じ)。
        /// mobile = true はシステムブラウザから来たネイティブアプリの要求で、サインイン後に Cookie ではなく使い捨てチケットを <see cref="ExternalLoginOptions.MobileCallbackUrl"/> へ返す。
        /// </summary>
        public IActionResult Challenge(ControllerBase controller, string provider, string? returnUrl, bool mobile = false, bool persistent = false)
        {
            var p = Find(provider);
            if (p == null) return controller.NotFound();
            if (mobile && !IsMobileEnabled) return controller.NotFound();

            //チャレンジのプロパティはそのまま Cookie のサインインに引き継がれる (IsPersistent もここで決まる)
            var properties = new AuthenticationProperties { RedirectUri = SanitizeReturnUrl(returnUrl), IsPersistent = persistent };
            if (mobile) properties.Items[MobileItem] = "1";
            return controller.Challenge(properties, SchemeOf(p.Name));
        }

        /// <summary>
        /// ログアウト時に IdP 側のセッションも終わらせる必要があるプロバイダ名。null なら Cookie を破棄するだけでよい。
        /// IdP への遷移は fetch (POST) からはできないので、アプリは名前を返してクライアントに GET のログアウト URL へ遷移させる (二段構え)。
        /// </summary>
        public async Task<string?> GetSignOutProviderAsync(ClaimsPrincipal user)
        {
            var p = Find(user.FindFirst(IdpClaimType)?.Value);
            if (p == null) return null;
            return await p.HasIdpSignOutAsync(_oidcOptions.Get(SchemeOf(p.Name))) ? p.Name : null;
        }

        /// <summary>Cookie を破棄し、IdP のセッションも終わらせて redirectUri (アプリ内パス) へ戻る。ブラウザ遷移 (GET) から呼ぶ。</summary>
        public async Task<IActionResult> SignOutAsync(ControllerBase controller, string provider, string redirectUri)
        {
            redirectUri = SanitizeReturnUrl(redirectUri);
            var p = Find(provider);
            if (p == null || await GetSignOutProviderAsync(controller.User) != p.Name)
            {
                //未設定・別経路のセッション (IdP 設定を外した後の旧 Cookie 等) は Cookie 破棄のみ
                await controller.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return controller.Redirect(redirectUri);
            }
            return await p.SignOutAsync(controller, SchemeOf(p.Name), redirectUri);
        }

        /// <summary>Cookie に積むプリンシパル。パスワードログインと同形 (Name / NameIdentifier) に、経路のプロバイダ名を足す。</summary>
        public ClaimsPrincipal CreatePrincipal(ExternalLoginUser user, string provider)
        {
            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.UserId));
            identity.AddClaim(new Claim(IdpClaimType, provider));
            return new ClaimsPrincipal(identity);
        }

        sealed class MobileTicket
        {
            public string UserId { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string Provider { get; set; } = string.Empty;
        }

        /// <summary>モバイル用の使い捨てチケットを発行し、コールバック URL を返す。</summary>
        internal async Task<string> IssueMobileTicketAsync(ExternalLoginUser user, string provider)
        {
            var ticket = Base64Url(RandomNumberGenerator.GetBytes(32));
            var value = JsonSerializer.Serialize(new MobileTicket { UserId = user.UserId, UserName = user.UserName, Provider = provider });
            await _cache.SetStringAsync(TicketKeyPrefix + ticket, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _options.MobileTicketLifetime });
            return MobileCallback("ticket", ticket);
        }

        /// <summary>チケットを 1 回だけユーザーに引き換える。期限切れ・不正・使用済みは null。アプリはこれで Cookie を発行する。</summary>
        public async Task<(ExternalLoginUser User, string Provider)?> RedeemMobileTicketAsync(string? ticket)
        {
            if (string.IsNullOrEmpty(ticket) || ticket.Length > 128) return null;
            var key = TicketKeyPrefix + ticket;
            var value = await _cache.GetStringAsync(key);
            if (value == null) return null;
            await _cache.RemoveAsync(key);
            var t = JsonSerializer.Deserialize<MobileTicket>(value);
            if (t == null || string.IsNullOrEmpty(t.UserId)) return null;
            return (new ExternalLoginUser(t.UserId, t.UserName), t.Provider);
        }

        internal string LoginErrorUrl(string error) => $"{_options.LoginPath}?error={Uri.EscapeDataString(error)}";

        internal string MobileCallback(string key, string value)
            => $"{_options.MobileCallbackUrl}{(_options.MobileCallbackUrl.Contains('?') ? "&" : "?")}{key}={Uri.EscapeDataString(value)}";

        //open redirect 防止でアプリ内相対パスのみ許可
        internal static string SanitizeReturnUrl(string? url)
            => string.IsNullOrEmpty(url) || !url.StartsWith('/') || url.StartsWith("//") || url.StartsWith("/\\") ? "/" : url;

        static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
