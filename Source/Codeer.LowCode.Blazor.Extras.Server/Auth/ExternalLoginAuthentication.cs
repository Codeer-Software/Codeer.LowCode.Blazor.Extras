using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 外部 IdP (<see cref="IExternalLoginProvider"/>) のサインインを Cookie 認証に差し込む。
    /// セッションは常に Cookie で、外部 IdP は本人確認の手段。確認できた本人をアプリのユーザーに解決するのは
    /// アプリが登録する <see cref="IExternalLoginUserResolver"/>。
    /// <code>
    /// builder.Services.AddDistributedMemoryCache();   // 使い捨てチケット / メール認証コードの置き場。複数インスタンスなら Redis 等
    /// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    ///     .AddCookie(...)
    ///     .AddExternalLogins(SystemConfig.Instance.ExternalLogins, o => o.MobileCallbackUrl = "lowcodeapp://auth");
    /// builder.Services.AddScoped&lt;IExternalLoginUserResolver, ExternalLoginUserResolver&gt;();
    /// </code>
    /// OpenID Connect ハンドラのイベントは、プロバイダの <see cref="IExternalLoginProvider.Configure"/> が設定したものを残して後ろに足す
    /// (順: プロバイダ → ここ。プロバイダが応答を処理済み / 失敗にしていればここは走らない)。
    /// ユーザーの解決は OnTokenValidated (id_token の署名・発行者・期限の検証の後)、Cookie の発行とネイティブアプリのチケット発行は
    /// OnTicketReceived (nonce・state など OpenID Connect の検証が全部終わった後) で行う。
    /// </summary>
    public static class ExternalLoginAuthentication
    {
        static readonly Regex ProviderName = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        //コールバックの 1 リクエストの中で OnTokenValidated → OnTicketReceived に渡す (Cookie には残さない)
        const string LoginNameItem = "codeer.extlogin.loginname";

        /// <summary>
        /// プロバイダごとに OpenID Connect スキーム ("ExternalLogin.{Name}") を登録する。どのプロバイダを並べるかはアプリの対応表 (テンプレートの ExternalLoginTable) が決める。
        /// プロバイダが 1 つも無くても <see cref="ExternalLoginService"/> は登録されるので、AccountController はそのまま動く。
        /// </summary>
        public static AuthenticationBuilder AddExternalLogins(this AuthenticationBuilder builder, IEnumerable<IExternalLoginProvider>? providers, Action<ExternalLoginOptions>? configure = null)
        {
            var options = new ExternalLoginOptions();
            configure?.Invoke(options);

            var list = (providers ?? []).ToList();
            foreach (var p in list)
            {
                if (!ProviderName.IsMatch(p.Name)) throw new InvalidOperationException($"External login: provider name '{p.Name}' may contain only letters, digits, '-' and '_'.");
            }
            var duplicated = list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicated != null) throw new InvalidOperationException($"External login: provider name '{duplicated.Key}' is registered more than once.");

            //モバイル用チケットの置き場は IDistributedCache。登録はアプリの責任 (テンプレートの CookieAuthentication.cs)。
            //単一インスタンスなら AddDistributedMemoryCache、複数インスタンス (スケールアウト) なら Redis / SQL Server 等の共有キャッシュ
            builder.Services.AddSingleton(sp => new ExternalLoginService(list, options,
                sp.GetService<IDistributedCache>() ?? throw new InvalidOperationException("External logins need an IDistributedCache. Register AddDistributedMemoryCache() (single instance) or a shared cache such as Redis (multiple instances) before AddExternalLogins."),
                sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()));

            foreach (var provider in list)
            {
                builder.AddOpenIdConnect(ExternalLoginService.SchemeOf(provider.Name), provider.DisplayName, o =>
                {
                    ApplyDefaults(o, provider.Name);
                    provider.Configure(o);
                    ChainEvents(o.Events, provider);
                });
            }
            return builder;
        }

        /// <summary>全プロバイダ共通の既定。プロバイダの <see cref="IExternalLoginProvider.Configure"/> はこの後に呼ばれるので上書きできる。</summary>
        static void ApplyDefaults(OpenIdConnectOptions options, string name)
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.CallbackPath = $"/signin-{name.ToLowerInvariant()}";
            options.SignedOutCallbackPath = $"/signout-{name.ToLowerInvariant()}";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            //クレーム名は id_token のまま扱う (email / name / preferred_username)。旧 SOAP 系 URI に変換しない
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "name";
            //end-session に id_token_hint を渡すため保存 (無いと IdP 側でアカウント選択画面が出る)
            options.SaveTokens = true;
        }

        //プロバイダが設定したイベントの後ろに共通処理を足す。プロバイダが Result を決めていれば (HandleResponse / Fail / Skip) 共通処理は走らない
        static void ChainEvents(OpenIdConnectEvents events, IExternalLoginProvider provider)
        {
            var tokenValidated = events.OnTokenValidated;
            events.OnTokenValidated = async ctx =>
            {
                await tokenValidated(ctx);
                if (ctx.Result != null) return;
                await OnTokenValidatedAsync(ctx, provider);
            };
            var ticketReceived = events.OnTicketReceived;
            events.OnTicketReceived = async ctx =>
            {
                await ticketReceived(ctx);
                if (ctx.Result != null) return;
                await OnTicketReceivedAsync(ctx, provider);
            };
            var remoteFailure = events.OnRemoteFailure;
            events.OnRemoteFailure = async ctx =>
            {
                await remoteFailure(ctx);
                if (ctx.Result != null) return;
                //IdP 側キャンセル・state 不一致など。詳細は漏らさずログイン画面へ差し戻す (理由は監査ログにだけ残す)
                await FailAsync(ctx.HttpContext, ctx.HandleResponse, provider.Name, ExternalLoginError.RemoteFailure, IsMobile(ctx.Properties), ctx.Failure?.Message);
            };
        }

        static bool IsMobile(AuthenticationProperties? properties)
            => properties?.Items.ContainsKey(ExternalLoginService.MobileItem) == true;

        //id_token の検証が通った時点: 本人をアプリのユーザーに解決してプリンシパルを差し替える。発行はまだ (nonce 等の検証がこの後にある)
        static async Task OnTokenValidatedAsync(TokenValidatedContext ctx, IExternalLoginProvider provider)
        {
            var mobile = IsMobile(ctx.Properties);
            var identity = provider.CreateIdentity(ctx.Principal!, out var error);
            if (identity == null)
            {
                await FailAsync(ctx.HttpContext, ctx.HandleResponse, provider.Name, string.IsNullOrEmpty(error) ? ExternalLoginError.InvalidClaims : error, mobile);
                return;
            }

            var resolver = ctx.HttpContext.RequestServices.GetService<IExternalLoginUserResolver>()
                ?? throw new InvalidOperationException($"Register {nameof(IExternalLoginUserResolver)} in DI to use external logins.");
            var user = await resolver.ResolveAsync(identity);
            if (user == null)
            {
                await FailAsync(ctx.HttpContext, ctx.HandleResponse, provider.Name, ExternalLoginError.UserNotRegistered, mobile, $"LoginName={identity.LoginName}");
                return;
            }

            ctx.HttpContext.Items[LoginNameItem] = identity.LoginName;
            ctx.Principal = LoginPrincipal.Create(user.UserId, user.UserName, provider.Name, user.SessionStamp);
        }

        //OpenID Connect の検証が全部通った時点: Web は監査に成立を書いて Cookie のサインインへ進む、ネイティブアプリは Cookie を発行せず使い捨てチケットをアプリへ返す
        static async Task OnTicketReceivedAsync(TicketReceivedContext ctx, IExternalLoginProvider provider)
        {
            //解決を経ていない (プロバイダが OnTokenValidated で自分のプリンシパルを通した) チケットには触らない。解決済みの印は同じリクエストで自分が置いた LoginNameItem
            if (ctx.HttpContext.Items[LoginNameItem] is not string loginName) return;
            var mobile = IsMobile(ctx.Properties);

            //TicketReceived は認証ハンドラの try の外で呼ばれる = ここで投げた例外は OnRemoteFailure に回らず未処理 (500) になるので、自分で差し戻す
            try
            {
                var principal = ctx.Principal;
                var userId = LoginPrincipal.GetUserId(principal);
                if (mobile)
                {
                    var service = ctx.HttpContext.RequestServices.GetRequiredService<ExternalLoginService>();
                    var user = new ExternalLoginUser(userId, LoginPrincipal.GetDisplayName(principal), LoginPrincipal.GetSessionStamp(principal));
                    ctx.Response.Redirect(await service.IssueMobileTicketAsync(user, provider.Name));
                    ctx.HandleResponse();
                    return;
                }

                //ログインの成立を記録する。書けなければ (Strict) 例外 = サインインしない (下の catch で差し戻す)。
                //ネイティブアプリの流れは上で抜けている (成立はチケットを Cookie に交換するアクションが記録する)
                await AuditAsync(ctx.HttpContext, AuditResult.Success, userId, $"Provider={provider.Name}; LoginName={loginName}");
            }
            catch (Exception ex)
            {
                await FailAsync(ctx.HttpContext, ctx.HandleResponse, provider.Name, ExternalLoginError.RemoteFailure, mobile, ex.Message);
            }
        }

        //サインインさせずログイン画面 (モバイルはアプリ) へ差し戻す
        static async Task FailAsync(HttpContext http, Action handleResponse, string provider, string error, bool mobile, string? detail = null)
        {
            await AuditAsync(http, AuditResult.Denied, string.Empty, $"Provider={provider}; Error={error}" + (string.IsNullOrEmpty(detail) ? string.Empty : $"; {detail}"));
            var service = http.RequestServices.GetRequiredService<ExternalLoginService>();
            http.Response.Redirect(mobile && service.IsMobileEnabled ? service.MobileCallback("error", error) : service.LoginErrorUrl(error));
            handleResponse();
        }

        //監査ログ: IdP からのコールバックは認証ハンドラが処理し、コントローラのアクションではないので AuditLogMiddleware には見えない。
        //ログインの成立と拒否はここで記録する (監査ログを結線していないアプリでは何もしない)
        static async Task AuditAsync(HttpContext http, AuditResult result, string userId, string detail)
        {
            var logger = http.RequestServices.GetService<AuditLogger>();
            if (logger == null) return;
            var e = new AuditEvent
            {
                Category = AuditCategory.Authentication, Action = "Account.ExternalLoginCallback", Result = result, UserId = userId, Detail = detail,
                DesignVersion = logger.GetDesignVersion(http),
            };
            AuditLogMiddleware.SetRequest(e, http);
            await logger.WriteAsync(e);
        }
    }
}
