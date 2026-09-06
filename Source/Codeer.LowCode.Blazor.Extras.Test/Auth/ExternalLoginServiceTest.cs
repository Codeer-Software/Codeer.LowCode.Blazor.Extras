using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>ExternalLoginService の IdP 非依存部分: 選択肢・Cookie プリンシパル・ログアウト分岐・モバイル用チケット。</summary>
    public class ExternalLoginServiceTest
    {
        static ExternalLoginService Create(ExternalLoginOptions? options = null, params IExternalLoginProvider[] providers)
        {
            var oidc = new ServiceCollection().AddOptions().BuildServiceProvider().GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
            var cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            return new ExternalLoginService(providers, options ?? new(), cache, oidc);
        }

        [Test]
        public void Options_NameAndDisplayName()
        {
            var service = Create(null,
                new EntraLoginProvider(new() { ClientId = "a" }),
                new GoogleLoginProvider(new() { ClientId = "b", DisplayName = "Google アカウント" }));

            Assert.That(service.Options.Select(o => o.Name), Is.EqualTo(new[] { "Entra", "Google" }));
            Assert.That(service.Options.Select(o => o.DisplayName), Is.EqualTo(new[] { "Entra", "Google アカウント" }));
            Assert.That(service.Find("entra")?.Name, Is.EqualTo("Entra"), "プロバイダ名は大文字小文字を区別しない");
            Assert.That(service.Find("Unknown"), Is.Null);
            Assert.That(service.IsMobileEnabled, Is.False);
            Assert.That(ExternalLoginService.SchemeOf("Entra"), Is.EqualTo("ExternalLogin.Entra"));
        }

        [Test]
        public void CreatePrincipal_HasSameShapeAsPasswordLoginPlusIdp()
        {
            var principal = Create().CreatePrincipal(new ExternalLoginUser("U1", "taro"), "Entra");

            Assert.That(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, Is.EqualTo("U1"));
            Assert.That(principal.FindFirst(ClaimTypes.Name)?.Value, Is.EqualTo("taro"));
            Assert.That(principal.FindFirst(ExternalLoginService.IdpClaimType)?.Value, Is.EqualTo("Entra"));
            Assert.That(principal.Identity?.AuthenticationType, Is.EqualTo("Cookies"));
        }

        [Test]
        public async Task GetSignOutProvider_DelegatesToProvider()
        {
            var service = Create(null,
                new EntraLoginProvider(new() { ClientId = "a" }),
                new GoogleLoginProvider(new() { ClientId = "b" }),
                new CognitoLoginProvider(new() { ClientId = "c", Authority = "https://cognito-idp.x.amazonaws.com/p" }),
                new CognitoLoginProvider(new() { ClientId = "c", Authority = "https://cognito-idp.x.amazonaws.com/p", Domain = "https://x.auth.ap-northeast-1.amazoncognito.com" }, "CognitoWithDomain"));

            Assert.That(await service.GetSignOutProviderAsync(service.CreatePrincipal(new("U", "u"), "Entra")), Is.EqualTo("Entra"));
            Assert.That(await service.GetSignOutProviderAsync(service.CreatePrincipal(new("U", "u"), "Google")), Is.Null, "Google に end-session は無い");
            Assert.That(await service.GetSignOutProviderAsync(service.CreatePrincipal(new("U", "u"), "Cognito")), Is.Null, "Domain 未設定はローカルログアウトのみ");
            Assert.That(await service.GetSignOutProviderAsync(service.CreatePrincipal(new("U", "u"), "CognitoWithDomain")), Is.EqualTo("CognitoWithDomain"));

            //パスワードログイン (idp クレーム無し) と、設定を外した後の旧 Cookie
            Assert.That(await service.GetSignOutProviderAsync(new ClaimsPrincipal(new ClaimsIdentity())), Is.Null);
            Assert.That(await service.GetSignOutProviderAsync(service.CreatePrincipal(new("U", "u"), "Removed")), Is.Null);
        }

        [Test]
        public async Task MobileTicket_IsOneTime()
        {
            var service = Create(new ExternalLoginOptions { MobileCallbackUrl = "lowcodeapp://auth" });
            Assert.That(service.IsMobileEnabled, Is.True);

            var url = await service.IssueMobileTicketAsync(new ExternalLoginUser("U1", "taro"), "Entra");
            Assert.That(url, Does.StartWith("lowcodeapp://auth?ticket="));
            var ticket = url.Substring("lowcodeapp://auth?ticket=".Length);
            Assert.That(ticket.Length, Is.GreaterThanOrEqualTo(40));

            var redeemed = await service.RedeemMobileTicketAsync(ticket);
            Assert.That(redeemed, Is.Not.Null);
            Assert.That(redeemed!.Value.User, Is.EqualTo(new ExternalLoginUser("U1", "taro")));
            Assert.That(redeemed.Value.Provider, Is.EqualTo("Entra"));

            Assert.That(await service.RedeemMobileTicketAsync(ticket), Is.Null, "2 回目は使えない");
            Assert.That(await service.RedeemMobileTicketAsync("nope"), Is.Null);
            Assert.That(await service.RedeemMobileTicketAsync(null), Is.Null);
            Assert.That(await service.RedeemMobileTicketAsync(new string('a', 200)), Is.Null);
        }

        [Test]
        public async Task MobileTicket_Expires()
        {
            var service = Create(new ExternalLoginOptions { MobileCallbackUrl = "lowcodeapp://auth", MobileTicketLifetime = TimeSpan.FromMilliseconds(50) });
            var url = await service.IssueMobileTicketAsync(new ExternalLoginUser("U1", "taro"), "Entra");
            var ticket = url.Substring(url.IndexOf("ticket=") + "ticket=".Length);
            await Task.Delay(200);
            Assert.That(await service.RedeemMobileTicketAsync(ticket), Is.Null);
        }

        [Test]
        public void MobileCallback_AppendsQueryCorrectly()
        {
            Assert.That(Create(new() { MobileCallbackUrl = "lowcodeapp://auth" }).MobileCallback("error", "user_not_registered"), Is.EqualTo("lowcodeapp://auth?error=user_not_registered"));
            Assert.That(Create(new() { MobileCallbackUrl = "lowcodeapp://auth?x=1" }).MobileCallback("error", "a b"), Is.EqualTo("lowcodeapp://auth?x=1&error=a%20b"));
            Assert.That(Create(new() { LoginPath = "/login.html" }).LoginErrorUrl(ExternalLoginError.RemoteFailure), Is.EqualTo("/login.html?error=remote_failure"));
        }
    }
}
