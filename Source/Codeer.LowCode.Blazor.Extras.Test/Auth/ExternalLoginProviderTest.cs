using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>プロバイダ実装ごとの Configure (Authority / スコープ / 検証) と CreateIdentity (クレームの取り方・検証) の単体テスト。IdP へは繋がない。</summary>
    public class ExternalLoginProviderTest
    {
        static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
            => new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "test"));

        static OpenIdConnectOptions Configure(IExternalLoginProvider provider)
        {
            var options = new OpenIdConnectOptions();
            provider.Configure(options);
            return options;
        }

        [Test]
        public void Oidc_RequiresNameAndAuthority()
        {
            Assert.Throws<InvalidOperationException>(() => new OidcLoginProvider(new() { ClientId = "id", Authority = "https://idp" }));
            Assert.Throws<InvalidOperationException>(() => new OidcLoginProvider(new() { Name = "Keycloak", ClientId = "id" }));
            Assert.Throws<InvalidOperationException>(() => new CognitoLoginProvider(new() { ClientId = "id" }));
            Assert.DoesNotThrow(() => new EntraLoginProvider(new() { ClientId = "id" }));
            Assert.DoesNotThrow(() => new GoogleLoginProvider(new() { ClientId = "id" }));
        }

        [Test]
        public void Oidc_ConfigureAndDisplayName()
        {
            var provider = new OidcLoginProvider(new() { Name = "Keycloak", ClientId = "id", ClientSecret = "secret", Authority = "https://idp.example.com/realms/x" });
            Assert.That(provider.Name, Is.EqualTo("Keycloak"));
            Assert.That(provider.DisplayName, Is.EqualTo("Keycloak"), "DisplayName 未指定は Name");

            var options = Configure(provider);
            Assert.That(options.Authority, Is.EqualTo("https://idp.example.com/realms/x"));
            Assert.That(options.ClientId, Is.EqualTo("id"));
            Assert.That(options.ClientSecret, Is.EqualTo("secret"));
            Assert.That(options.Scope, Is.EquivalentTo(new[] { "openid", "email", "profile" }));

            var custom = Configure(new OidcLoginProvider(new() { Name = "K", DisplayName = "社内", ClientId = "id", Authority = "https://idp", Scopes = ["openid", "roles"] }));
            Assert.That(custom.Scope, Is.EquivalentTo(new[] { "openid", "roles" }));
        }

        [Test]
        public void Oidc_LoginNameFallbackOrder_AndOverride()
        {
            var provider = new OidcLoginProvider(new() { Name = "K", ClientId = "id", Authority = "https://idp" });
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "u1"), ("email", "e@x")), out _)?.LoginName, Is.EqualTo("u1"));
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("email", "e@x")), out _)?.LoginName, Is.EqualTo("e@x"));
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1")), out _)?.LoginName, Is.EqualTo("S1"));

            var byClaim = new OidcLoginProvider(new() { Name = "K", ClientId = "id", Authority = "https://idp", LoginNameClaim = "employee_id" });
            Assert.That(byClaim.CreateIdentity(Principal(("sub", "S1"), ("employee_id", "E-9"), ("email", "e@x")), out _)?.LoginName, Is.EqualTo("E-9"));
            Assert.That(byClaim.CreateIdentity(Principal(("sub", "S1"), ("email", "e@x")), out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ExternalLoginError.InvalidClaims));
        }

        [Test]
        public void Oidc_MissingSubject_IsInvalid()
        {
            var provider = new OidcLoginProvider(new() { Name = "K", ClientId = "id", Authority = "https://idp" });
            Assert.That(provider.CreateIdentity(Principal(("email", "e@x")), out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ExternalLoginError.InvalidClaims));
        }

        [Test]
        public void Oidc_IdentityCarriesAllInfo()
        {
            var provider = new OidcLoginProvider(new() { Name = "K", ClientId = "id", Authority = "https://idp" });
            var identity = provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "taro"), ("email", "t@x"), ("name", "Taro"), ("iss", "https://idp")), out _);
            Assert.That(identity, Is.Not.Null);
            Assert.That(identity!.Provider, Is.EqualTo("K"));
            Assert.That(identity.Subject, Is.EqualTo("S1"));
            Assert.That(identity.Email, Is.EqualTo("t@x"));
            Assert.That(identity.DisplayName, Is.EqualTo("Taro"));
            Assert.That(identity.Issuer, Is.EqualTo("https://idp"));
            Assert.That(identity.Claims.FindFirst("name")?.Value, Is.EqualTo("Taro"));
        }

        [Test]
        public void AllowedDomains()
        {
            var provider = new EntraLoginProvider(new() { ClientId = "id", AllowedDomains = ["example.co.jp", " Example.com "] });
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "a@example.co.jp")), out _), Is.Not.Null);
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "a@EXAMPLE.com")), out _), Is.Not.Null);
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "a@other.com")), out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ExternalLoginError.DomainNotAllowed));
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "nodomain")), out _), Is.Null);
        }

        [Test]
        public void Entra_Authority_DefaultsToOrganizations_TenantIdWhenGiven()
        {
            Assert.That(Configure(new EntraLoginProvider(new() { ClientId = "id" })).Authority, Is.EqualTo("https://login.microsoftonline.com/organizations/v2.0"));
            Assert.That(Configure(new EntraLoginProvider(new() { ClientId = "id", TenantId = "11111111-2222-3333-4444-555555555555" })).Authority,
                Is.EqualTo("https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0"));
            Assert.That(new EntraLoginProvider(new() { ClientId = "id" }).Name, Is.EqualTo("Entra"));
            Assert.That(new EntraLoginProvider(new() { ClientId = "id", DisplayName = "職場アカウント" }).DisplayName, Is.EqualTo("職場アカウント"));
        }

        [Test]
        public void Entra_IssuerValidator_OnlyForMultiTenant()
        {
            //organizations / common は issuer がテナントごとに違うので形式検証
            Assert.That(Configure(new EntraLoginProvider(new() { ClientId = "id" })).TokenValidationParameters.IssuerValidator, Is.Not.Null);
            Assert.That(Configure(new EntraLoginProvider(new() { ClientId = "id", TenantId = "common" })).TokenValidationParameters.IssuerValidator, Is.Not.Null);
            //単一テナントは discovery の issuer と突き合わせる既定のまま
            Assert.That(Configure(new EntraLoginProvider(new() { ClientId = "id", TenantId = "11111111-2222-3333-4444-555555555555" })).TokenValidationParameters.IssuerValidator, Is.Null);
        }

        [Test]
        public void Entra_ValidateIssuer_AcceptsTenantGuid_RejectsOthers()
        {
            var ok = "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0";
            Assert.That(EntraLoginProvider.ValidateIssuer(ok, null!, new TokenValidationParameters()), Is.EqualTo(ok));
            Assert.Throws<SecurityTokenInvalidIssuerException>(() => EntraLoginProvider.ValidateIssuer("https://login.microsoftonline.com/common/v2.0", null!, new()));
            Assert.Throws<SecurityTokenInvalidIssuerException>(() => EntraLoginProvider.ValidateIssuer("https://evil.example.com/11111111-2222-3333-4444-555555555555/v2.0", null!, new()));
            Assert.Throws<SecurityTokenInvalidIssuerException>(() => EntraLoginProvider.ValidateIssuer("https://sts.windows.net/11111111-2222-3333-4444-555555555555/", null!, new()));
        }

        [Test]
        public void Entra_UsesUpnNotEmail()
        {
            var provider = new EntraLoginProvider(new() { ClientId = "id" });
            var identity = provider.CreateIdentity(Principal(("sub", "S1"), ("preferred_username", "taro@example.co.jp"), ("email", "spoofed@other.com")), out _);
            Assert.That(identity?.LoginName, Is.EqualTo("taro@example.co.jp"));
            Assert.That(identity?.Email, Is.EqualTo("spoofed@other.com"));
        }

        [Test]
        public void Entra_GuestRejectedUnlessAllowed()
        {
            var principal = Principal(("sub", "S1"), ("preferred_username", "guest_gmail.com#EXT#@tenant.onmicrosoft.com"));
            Assert.That(new EntraLoginProvider(new() { ClientId = "id" }).CreateIdentity(principal, out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ExternalLoginError.GuestNotAllowed));
            Assert.That(new EntraLoginProvider(new() { ClientId = "id", AllowGuests = true }).CreateIdentity(principal, out _), Is.Not.Null);
        }

        [Test]
        public async Task Entra_AlwaysSignsOutIdp()
        {
            Assert.That(await new EntraLoginProvider(new() { ClientId = "id" }).HasIdpSignOutAsync(new OpenIdConnectOptions()), Is.True);
        }

        [Test]
        public async Task Google_RequiresVerifiedEmail_NoIdpSignOut()
        {
            var provider = new GoogleLoginProvider(new() { ClientId = "id" });
            Assert.That(Configure(provider).Authority, Is.EqualTo("https://accounts.google.com"));

            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("email", "a@gmail.com"), ("email_verified", "false")), out var error), Is.Null);
            Assert.That(error, Is.EqualTo(ExternalLoginError.InvalidClaims));
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("email", "a@gmail.com")), out _), Is.Null);
            Assert.That(provider.CreateIdentity(Principal(("sub", "S1"), ("email", "a@gmail.com"), ("email_verified", "true")), out _)?.LoginName, Is.EqualTo("a@gmail.com"));

            Assert.That(await provider.HasIdpSignOutAsync(new OpenIdConnectOptions()), Is.False, "Google に end-session は無い");
        }

        [Test]
        public async Task Cognito_EmailVerified_LoginNameClaimOverride_SignOutNeedsDomain()
        {
            var principal = Principal(("sub", "S1"), ("cognito:username", "taro"), ("email", "a@example.com"), ("email_verified", "false"));

            var byEmail = new CognitoLoginProvider(new() { ClientId = "id", Authority = "https://cognito-idp.ap-northeast-1.amazonaws.com/ap-northeast-1_abc" });
            Assert.That(Configure(byEmail).Authority, Is.EqualTo("https://cognito-idp.ap-northeast-1.amazonaws.com/ap-northeast-1_abc"));
            Assert.That(byEmail.CreateIdentity(principal, out _), Is.Null, "未検証メールは拒否");
            Assert.That(await byEmail.HasIdpSignOutAsync(new OpenIdConnectOptions()), Is.False, "Domain 未設定はローカルログアウトのみ");

            var byUserName = new CognitoLoginProvider(new() { ClientId = "id", Authority = "https://cognito-idp.x.amazonaws.com/p", LoginNameClaim = "cognito:username", Domain = "https://my.auth.ap-northeast-1.amazoncognito.com/" });
            Assert.That(byUserName.CreateIdentity(principal, out _)?.LoginName, Is.EqualTo("taro"));
            Assert.That(await byUserName.HasIdpSignOutAsync(new OpenIdConnectOptions()), Is.True);
        }

        [Test]
        public void SanitizeReturnUrl_AllowsOnlyAppRelativePaths()
        {
            Assert.That(ExternalLoginService.SanitizeReturnUrl(null), Is.EqualTo("/"));
            Assert.That(ExternalLoginService.SanitizeReturnUrl(""), Is.EqualTo("/"));
            Assert.That(ExternalLoginService.SanitizeReturnUrl("/page/1?x=1"), Is.EqualTo("/page/1?x=1"));
            Assert.That(ExternalLoginService.SanitizeReturnUrl("//evil.com"), Is.EqualTo("/"));
            Assert.That(ExternalLoginService.SanitizeReturnUrl("/\\evil.com"), Is.EqualTo("/"));
            Assert.That(ExternalLoginService.SanitizeReturnUrl("https://evil.com"), Is.EqualTo("/"));
        }
    }
}
