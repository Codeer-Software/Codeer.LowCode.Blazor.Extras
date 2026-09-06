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
    /// builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    ///     .AddCookie(...)
    ///     .AddExternalLogins(SystemConfig.Instance.ExternalLogins, o => o.MobileCallbackUrl = "lowcodeapp://auth");
    /// builder.Services.AddScoped&lt;IExternalLoginUserResolver, ExternalLoginUserResolver&gt;();
    /// </code>
    /// </summary>
    public static class ExternalLoginAuthentication
    {
        static readonly Regex ProviderName = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

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

            //モバイル用チケットの置き場。アプリが Redis 等の IDistributedCache を登録していればそれが使われる (複数インスタンス構成)
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSingleton(sp => new ExternalLoginService(list, options,
                sp.GetRequiredService<IDistributedCache>(), sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()));

            foreach (var provider in list)
            {
                builder.AddOpenIdConnect(ExternalLoginService.SchemeOf(provider.Name), provider.DisplayName, o =>
                {
                    ApplyDefaults(o, provider.Name);
                    provider.Configure(o);
                    o.Events = new OpenIdConnectEvents
                    {
                        OnTokenValidated = ctx => OnTokenValidatedAsync(ctx, provider),
                        OnRemoteFailure = ctx =>
                        {
                            //IdP 側キャンセル・state 不一致など。詳細は漏らさずログイン画面へ差し戻す
                            Fail(ctx.HttpContext, ctx.HandleResponse, ExternalLoginError.RemoteFailure, IsMobile(ctx.Properties));
                            return Task.CompletedTask;
                        },
                    };
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

        static bool IsMobile(AuthenticationProperties? properties)
            => properties?.Items.ContainsKey(ExternalLoginService.MobileItem) == true;

        static async Task OnTokenValidatedAsync(TokenValidatedContext ctx, IExternalLoginProvider provider)
        {
            var mobile = IsMobile(ctx.Properties);
            var identity = provider.CreateIdentity(ctx.Principal!, out var error);
            if (identity == null)
            {
                Fail(ctx.HttpContext, ctx.HandleResponse, string.IsNullOrEmpty(error) ? ExternalLoginError.InvalidClaims : error, mobile);
                return;
            }

            var resolver = ctx.HttpContext.RequestServices.GetService<IExternalLoginUserResolver>()
                ?? throw new InvalidOperationException($"Register {nameof(IExternalLoginUserResolver)} in DI to use external logins.");
            var user = await resolver.ResolveAsync(identity);
            if (user == null)
            {
                Fail(ctx.HttpContext, ctx.HandleResponse, ExternalLoginError.UserNotRegistered, mobile);
                return;
            }

            var service = ctx.HttpContext.RequestServices.GetRequiredService<ExternalLoginService>();
            if (mobile)
            {
                //ネイティブアプリ: システムブラウザには Cookie を発行せず、使い捨てチケットをアプリへ返す
                ctx.Response.Redirect(await service.IssueMobileTicketAsync(user, provider.Name));
                ctx.HandleResponse();
                return;
            }

            //Cookie にはパスワードログインと同形の最小クレームだけを積む
            ctx.Principal = service.CreatePrincipal(user, provider.Name);
        }

        //サインインさせずログイン画面 (モバイルはアプリ) へ差し戻す
        static void Fail(HttpContext http, Action handleResponse, string error, bool mobile)
        {
            var service = http.RequestServices.GetRequiredService<ExternalLoginService>();
            http.Response.Redirect(mobile && service.IsMobileEnabled ? service.MobileCallback("error", error) : service.LoginErrorUrl(error));
            handleResponse();
        }
    }
}
