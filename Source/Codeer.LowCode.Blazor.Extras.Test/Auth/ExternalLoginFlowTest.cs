using Microsoft.Extensions.Logging;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Test.AuditLog;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.Caching.Distributed;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// 偽 IdP (discovery / jwks / authorize / token / end-session) とアプリを TestServer で立て、
    /// 本物の OpenIdConnect ハンドラを通して「チャレンジ → IdP → コールバック → ユーザー解決 → Cookie → ログアウト」を検証する。
    /// アプリ側のエンドポイントはテンプレートの AccountController と同じ形 (TestAccountController)。
    /// </summary>
    //MVC はネストしたクラスをコントローラとして拾わない (IsPublic=false) のでトップレベルに置く
    public class LoginTicket { public string? Ticket { get; set; } }
    public class LogoutResult { public string? Redirect { get; set; } }

    [ApiController, Route("api/account")]
    public class TestAccountController : ControllerBase
    {
        readonly ExternalLoginService _externalLogins;
        public TestAccountController(ExternalLoginService externalLogins) => _externalLogins = externalLogins;

        [Authorize, HttpGet("current_user")]
        public string CurrentUser() => LoginPrincipal.GetUserId(User);

        //テスト用: Cookie に積まれたセッションの印
        [Authorize, HttpGet("session_stamp")]
        public string SessionStamp() => LoginPrincipal.GetSessionStamp(User) ?? string.Empty;

        [HttpGet("login_options")]
        public object LoginOptions() => new { Password = true, Providers = _externalLogins.Options };

        [HttpGet("login/{provider}")]
        public IActionResult ExternalLogin(string provider, string? returnUrl, bool mobile = false, bool persistent = false)
            => _externalLogins.Challenge(this, provider, returnUrl, mobile, persistent);

        [HttpPost("login_ticket")]
        public async Task<IActionResult> LoginTicket(LoginTicket ticket)
        {
            var redeemed = await _externalLogins.RedeemMobileTicketAsync(ticket.Ticket);
            if (redeemed == null) return Unauthorized();
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, LoginPrincipal.Create(redeemed.Value.User.UserId, redeemed.Value.User.UserName, redeemed.Value.Provider, redeemed.Value.User.SessionStamp));
            return Ok();
        }

        [Authorize, HttpPost("logout")]
        public async Task<IActionResult> Logout(bool mobile = false)
        {
            if (!mobile)
            {
                var provider = await _externalLogins.GetSignOutProviderAsync(User);
                if (provider != null) return Ok(new LogoutResult { Redirect = $"/api/account/logout/{provider}" });
            }
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new LogoutResult());
        }

        [HttpGet("logout/{provider}")]
        public Task<IActionResult> ExternalLogout(string provider) => _externalLogins.SignOutAsync(this, provider, "/login.html");
    }

    public class ExternalLoginFlowTest
    {
        const string AppBase = "https://app.test";

        //---- 偽 IdP -------------------------------------------------------------------------

        sealed class FakeIdp : IAsyncDisposable
        {
            readonly RSA _rsa = RSA.Create(2048);
            readonly Dictionary<string, string> _nonces = new();
            WebApplication? _app;

            public string Issuer { get; init; } = "https://idp.test";
            public bool HasEndSession { get; init; } = true;
            /// <summary>次に発行する id_token のクレーム (sub 以外)。</summary>
            public Dictionary<string, object> Claims { get; } = new();
            public string Subject { get; set; } = "sub-1";
            /// <summary>設定すると id_token の nonce をこの値にする (認可要求の nonce と食い違わせる)。</summary>
            public string? NonceOverride { get; set; }
            public Dictionary<string, string> LastAuthorizeQuery { get; private set; } = new();
            public Dictionary<string, string> LastLogoutQuery { get; private set; } = new();
            public Dictionary<string, string> LastTokenForm { get; private set; } = new();
            public TestServer Server => _app!.GetTestServer();

            public async Task StartAsync()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                _app = builder.Build();
                _app.Run(async ctx =>
                {
                    var path = ctx.Request.Path.Value ?? string.Empty;
                    if (path.EndsWith("/.well-known/openid-configuration"))
                    {
                        var doc = new Dictionary<string, object>
                        {
                            ["issuer"] = Issuer,
                            ["authorization_endpoint"] = "https://idp.test/authorize",
                            ["token_endpoint"] = "https://idp.test/token",
                            ["jwks_uri"] = "https://idp.test/jwks",
                            ["response_types_supported"] = new[] { "code" },
                            ["subject_types_supported"] = new[] { "public" },
                            ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                            ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post" },
                        };
                        if (HasEndSession) doc["end_session_endpoint"] = "https://idp.test/logout";
                        await ctx.Response.WriteAsJsonAsync(doc);
                    }
                    else if (path == "/jwks")
                    {
                        var p = _rsa.ExportParameters(false);
                        await ctx.Response.WriteAsJsonAsync(new
                        {
                            keys = new[] { new { kty = "RSA", use = "sig", kid = "k1", alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } }
                        });
                    }
                    else if (path == "/authorize")
                    {
                        LastAuthorizeQuery = ctx.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
                        var code = Guid.NewGuid().ToString("N");
                        _nonces[code] = LastAuthorizeQuery["nonce"];
                        ctx.Response.Redirect($"{LastAuthorizeQuery["redirect_uri"]}?code={code}&state={Uri.EscapeDataString(LastAuthorizeQuery["state"])}");
                    }
                    else if (path == "/token")
                    {
                        var form = await ctx.Request.ReadFormAsync();
                        LastTokenForm = form.ToDictionary(f => f.Key, f => f.Value.ToString());
                        var accessToken = "access-" + Guid.NewGuid().ToString("N");
                        var idToken = CreateIdToken(form["client_id"]!, NonceOverride ?? _nonces[form["code"]!], accessToken);
                        await ctx.Response.WriteAsJsonAsync(new { id_token = idToken, access_token = accessToken, token_type = "Bearer", expires_in = 3600 });
                    }
                    else if (path == "/logout")
                    {
                        LastLogoutQuery = ctx.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
                        var back = LastLogoutQuery["post_logout_redirect_uri"];
                        if (LastLogoutQuery.TryGetValue("state", out var state)) back += "?state=" + Uri.EscapeDataString(state);
                        ctx.Response.Redirect(back);
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                    }
                });
                await _app.StartAsync();
                Server.BaseAddress = new Uri("https://idp.test");
            }

            string CreateIdToken(string audience, string nonce, string accessToken)
            {
                var key = new RsaSecurityKey(_rsa) { KeyId = "k1" };
                var claims = new List<Claim> { new("sub", Subject), new("nonce", nonce), new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
                foreach (var (k, v) in Claims)
                {
                    claims.Add(v is bool b ? new Claim(k, b ? "true" : "false", ClaimValueTypes.Boolean) : new Claim(k, v.ToString()!));
                }
                //at_hash = access_token の SHA-256 左半分
                var hash = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(accessToken));
                claims.Add(new Claim("at_hash", Base64UrlEncoder.Encode(hash, 0, hash.Length / 2)));
                var token = new JwtSecurityToken(Issuer, audience, claims, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
                    new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                return new JwtSecurityTokenHandler().WriteToken(token);
            }

            public async ValueTask DisposeAsync()
            {
                if (_app != null) await _app.DisposeAsync();
                _rsa.Dispose();
            }
        }

        /// <summary>アプリのバックチャネル (discovery / jwks / token) をホストに関わらず偽 IdP へ流す。Entra のように Authority が別ホストでも同じ偽 IdP が応える。</summary>
        sealed class ToFakeIdpHandler : DelegatingHandler
        {
            public ToFakeIdpHandler(HttpMessageHandler inner) : base(inner) { }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.RequestUri = new Uri("https://idp.test" + request.RequestUri!.PathAndQuery);
                return base.SendAsync(request, cancellationToken);
            }
        }

        //---- アプリ (テンプレートの AccountController と同じ形) ---------------------------------

        public sealed class FakeResolver : IExternalLoginUserResolver
        {
            public static readonly Dictionary<string, string> Users = new();
            public static ExternalLoginIdentity? Last;
            /// <summary>設定するとユーザー表 (SQLite) から解決する (テンプレートの ExternalLoginUserResolver と同じ: 印も返す)。</summary>
            public static LoginAccountStore? Accounts;
            public async Task<ExternalLoginUser?> ResolveAsync(ExternalLoginIdentity identity)
            {
                Last = identity;
                if (Accounts != null)
                {
                    var account = await Accounts.FindByExternalLoginNameAsync(identity.LoginName);
                    return account == null ? null : new ExternalLoginUser(account.UserId, account.DisplayName, account.SessionStamp);
                }
                return Users.TryGetValue(identity.LoginName, out var id) ? new ExternalLoginUser(id, identity.LoginName) : null;
            }
        }


        sealed class App : IAsyncDisposable
        {
            WebApplication? _app;
            public TestServer Server => _app!.GetTestServer();
            public static string? LastRemoteFailure;

            /// <param name="auditSink">渡すと監査ログを結線する (テンプレートと同じ並び: UseRouting → UseAuditLog → 認証)。</param>
            /// <param name="sessionAccounts">渡すと Cookie の OnValidatePrincipal に LoginSessionValidation を結線する (テンプレートの CookieAuthentication.cs と同じ)。</param>
            public async Task StartAsync(FakeIdp idp, IEnumerable<IExternalLoginProvider> providers, string mobileCallback = "", IAuditSink? auditSink = null,
                LoginAccountStore? sessionAccounts = null, IDistributedCache? cache = null)
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                //OpenIdConnectPostConfigureOptions より先に登録して、バックチャネルを偽 IdP に向ける
                foreach (var p in providers)
                {
                    builder.Services.PostConfigure<OpenIdConnectOptions>(ExternalLoginService.SchemeOf(p.Name), o => o.Backchannel = new HttpClient(new ToFakeIdpHandler(idp.Server.CreateHandler())));
                }
                builder.Services.AddDistributedMemoryCache();
                if (cache != null) builder.Services.AddSingleton(cache);   //後に登録したものが使われる
                builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(o =>
                    {
                        o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
                        if (sessionAccounts != null) o.Events.OnValidatePrincipal = c => LoginSessionValidation.ValidatePrincipalAsync(c, sessionAccounts);
                    })
                    .AddExternalLogins(providers, o => o.MobileCallbackUrl = mobileCallback);
                builder.Services.AddScoped<IExternalLoginUserResolver, FakeResolver>();
                //診断: リモート失敗の理由を残す (OpenIdConnectPostConfigureOptions の後に走るので Events は設定済み)
                foreach (var p in providers)
                {
                    builder.Services.PostConfigure<OpenIdConnectOptions>(ExternalLoginService.SchemeOf(p.Name), o =>
                    {
                        var original = o.Events.OnRemoteFailure;
                        o.Events.OnRemoteFailure = ctx => { LastRemoteFailure = ctx.Failure?.ToString(); return original(ctx); };
                    });
                }
                builder.Services.AddControllers().AddApplicationPart(typeof(TestAccountController).Assembly);
                if (auditSink != null) builder.Services.AddAuditLog(new AuditLogSettings { Enabled = true }, [auditSink]);

                _app = builder.Build();
                if (auditSink != null)
                {
                    _app.UseRouting();
                    _app.UseAuditLog();
                }
                _app.UseAuthentication();
                _app.UseAuthorization();
                _app.MapControllers();
                _app.MapGet("/login.html", () => "login page");
                await _app.StartAsync();
                Server.BaseAddress = new Uri(AppBase);
            }

            public async ValueTask DisposeAsync()
            {
                if (_app != null) await _app.DisposeAsync();
            }
        }

        /// <summary>Cookie を持ち、ホストでアプリ / 偽 IdP を振り分けるブラウザ役。リダイレクトは自動で追わない (検証のため)。</summary>
        sealed class Browser
        {
            readonly CookieContainer _cookies = new();
            readonly HttpClient _app;
            readonly HttpClient _idp;

            public Browser(App app, FakeIdp idp)
            {
                _app = app.Server.CreateClient();
                _idp = idp.Server.CreateClient();
            }

            public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null)
            {
                var uri = new Uri(url.StartsWith("http") ? url : AppBase + url);
                var request = new HttpRequestMessage(method, uri) { Content = content };
                var cookie = _cookies.GetCookieHeader(uri);
                if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
                var client = uri.Host == "idp.test" ? _idp : _app;
                var response = await client.SendAsync(request);
                LastSetCookies = response.Headers.TryGetValues("Set-Cookie", out var setCookies) ? setCookies.ToList() : new();
                foreach (var c in LastSetCookies) _cookies.SetCookies(uri, c);
                return response;
            }

            /// <summary>直近の応答の Set-Cookie (Cookie の属性を見るため)。</summary>
            public List<string> LastSetCookies { get; private set; } = new();

            public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);
            public Task<HttpResponseMessage> PostAsync(string url, object body) => SendAsync(HttpMethod.Post, url, JsonContent.Create(body));

            /// <summary>チャレンジから IdP を経由してアプリのコールバックまで進め、コールバックの応答を返す。</summary>
            public async Task<HttpResponseMessage> SignInThroughIdpAsync(string challengeUrl)
            {
                var challenge = await GetAsync(challengeUrl);
                Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await challenge.Content.ReadAsStringAsync());
                var authorize = challenge.Headers.Location!.ToString();
                Assert.That(authorize, Does.StartWith("https://idp.test/authorize?"));

                var idp = await GetAsync(authorize);
                Assert.That(idp.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), authorize + " " + await idp.Content.ReadAsStringAsync());
                var callback = idp.Headers.Location!.ToString();
                Assert.That(callback, Does.StartWith(AppBase + "/signin-"));

                return await GetAsync(callback);
            }

            public bool HasAuthCookie => _cookies.GetCookies(new Uri(AppBase)).Any(c => c.Name.StartsWith(".AspNetCore.Cookies"));
        }

        static Dictionary<string, string> Query(string url) => HttpUtility.ParseQueryString(new Uri(url).Query).AllKeys
            .Where(k => k != null).ToDictionary(k => k!, k => HttpUtility.ParseQueryString(new Uri(url).Query)[k]!);

        [SetUp]
        public void SetUp()
        {
            FakeResolver.Users.Clear();
            FakeResolver.Last = null;
            FakeResolver.Accounts = null;
            App.LastRemoteFailure = null;
        }

        //---- テスト -------------------------------------------------------------------------

        [Test]
        public async Task Oidc_SignIn_ThenTwoStepLogoutEndsIdpSession()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            idp.Claims["email"] = "taro@example.com";
            idp.Claims["name"] = "Taro";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", ClientSecret = "secret", Authority = "https://idp.test", DisplayName = "社内" })]);
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            //ログイン画面の選択肢
            var options = await (await browser.GetAsync("/api/account/login_options")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.That(options.GetProperty("providers")[0].GetProperty("displayName").GetString(), Is.EqualTo("社内"));

            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Test?returnUrl=/page/1");
            Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await callback.Content.ReadAsStringAsync());
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/page/1"));
            Assert.That(browser.HasAuthCookie, Is.True);

            //IdP に送った内容: リダイレクト URI / スコープ / PKCE。トークン交換はシークレット付き
            Assert.That(idp.LastAuthorizeQuery["redirect_uri"], Is.EqualTo(AppBase + "/signin-test"));
            Assert.That(idp.LastAuthorizeQuery["scope"], Is.EqualTo("openid email profile"));
            Assert.That(idp.LastAuthorizeQuery.ContainsKey("code_challenge"), Is.True);
            Assert.That(idp.LastTokenForm["client_secret"], Is.EqualTo("secret"));
            Assert.That(idp.LastTokenForm.ContainsKey("code_verifier"), Is.True);

            //解決に渡った本人情報
            Assert.That(FakeResolver.Last!.LoginName, Is.EqualTo("taro"));
            Assert.That(FakeResolver.Last.Subject, Is.EqualTo("sub-1"));
            Assert.That(FakeResolver.Last.Email, Is.EqualTo("taro@example.com"));
            Assert.That(FakeResolver.Last.DisplayName, Is.EqualTo("Taro"));
            Assert.That(FakeResolver.Last.Issuer, Is.EqualTo("https://idp.test"));

            //Cookie でユーザー ID が取れる
            var me = await browser.GetAsync("/api/account/current_user");
            Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await me.Content.ReadAsStringAsync(), Is.EqualTo("U1"));

            //ログアウト二段構え: POST は遷移先を返し、GET で Cookie 破棄 + IdP の end-session へ
            var logout = await browser.PostAsync("/api/account/logout", new { });
            var result = await logout.Content.ReadFromJsonAsync<LogoutResult>();
            Assert.That(result!.Redirect, Is.EqualTo("/api/account/logout/Test"));

            var endSession = await browser.GetAsync(result.Redirect!);
            Assert.That(endSession.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            var location = endSession.Headers.Location!.ToString();
            Assert.That(location, Does.StartWith("https://idp.test/logout?"));
            var q = Query(location);
            Assert.That(q["post_logout_redirect_uri"], Is.EqualTo(AppBase + "/signout-test"));
            Assert.That(q.ContainsKey("id_token_hint"), Is.True, "SaveTokens で保持した id_token をヒントに渡す");
            Assert.That(browser.HasAuthCookie, Is.False);
            Assert.That((await browser.GetAsync("/api/account/current_user")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            //IdP から戻ってきたらログイン画面へ
            var back = await browser.GetAsync(location);
            var signedOut = await browser.GetAsync(back.Headers.Location!.ToString());
            Assert.That(signedOut.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(signedOut.Headers.Location!.ToString(), Is.EqualTo("/login.html"));
        }

        [Test]
        public async Task Persistent_MakesTheCookieSurviveTheBrowserSession()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })]);
            FakeResolver.Users["taro"] = "U1";

            //既定はセッション Cookie (expires 無し)
            var session = new Browser(app, idp);
            await session.SignInThroughIdpAsync("/api/account/login/Test");
            var sessionCookie = session.LastSetCookies.Single(c => c.StartsWith(".AspNetCore.Cookies="));
            Assert.That(sessionCookie, Does.Not.Contain("expires="));

            //「ログイン状態を保持する」= persistent: 有効期限付き
            var persistent = new Browser(app, idp);
            await persistent.SignInThroughIdpAsync("/api/account/login/Test?persistent=true");
            var persistentCookie = persistent.LastSetCookies.Single(c => c.StartsWith(".AspNetCore.Cookies="));
            Assert.That(persistentCookie, Does.Contain("expires="));
            Assert.That(await (await persistent.GetAsync("/api/account/current_user")).Content.ReadAsStringAsync(), Is.EqualTo("U1"));
        }

        [Test]
        public async Task Oidc_WithoutEndSession_LogoutIsLocalOnly()
        {
            await using var idp = new FakeIdp { HasEndSession = false };
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })]);
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            await browser.SignInThroughIdpAsync("/api/account/login/Test");
            var logout = await browser.PostAsync("/api/account/logout", new { });
            var result = await logout.Content.ReadFromJsonAsync<LogoutResult>();
            Assert.That(result!.Redirect, Is.Null);
            Assert.That(browser.HasAuthCookie, Is.False);
        }

        [Test]
        public async Task UnregisteredUser_IsSentBackToLoginPageWithoutCookie()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "stranger";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })]);
            var browser = new Browser(app, idp);

            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Test");
            Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=user_not_registered"));
            Assert.That(browser.HasAuthCookie, Is.False);
            Assert.That(FakeResolver.Last!.LoginName, Is.EqualTo("stranger"));
        }

        //IdP からのコールバックは認証ハンドラが処理する (コントローラのアクションではない) ので、ログインの成立と拒否は外部ログインの部品が監査ログに書く
        [Test]
        public async Task Audit_CallbackRecordsSignedInUser()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var sink = new CapturingAuditSink();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], auditSink: sink);
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            await browser.SignInThroughIdpAsync("/api/account/login/Test");

            Assert.That(browser.HasAuthCookie, Is.True);
            var callback = sink.Events.Single(e => e.Action == "Account.ExternalLoginCallback");
            Assert.That(callback.Category, Is.EqualTo(AuditCategory.Authentication));
            Assert.That(callback.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(callback.UserId, Is.EqualTo("U1"));
            Assert.That(callback.Detail, Is.EqualTo("Provider=Test; LoginName=taro"));
            Assert.That(callback.RequestId, Is.Not.Empty);
        }

        [Test]
        public async Task Audit_CallbackRecordsRejectedUserAsDenied()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "stranger";
            await idp.StartAsync();
            var sink = new CapturingAuditSink();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], auditSink: sink);
            var browser = new Browser(app, idp);

            await browser.SignInThroughIdpAsync("/api/account/login/Test");

            Assert.That(browser.HasAuthCookie, Is.False);
            var callback = sink.Events.Single(e => e.Action == "Account.ExternalLoginCallback");
            Assert.That(callback.Result, Is.EqualTo(AuditResult.Denied));
            Assert.That(callback.UserId, Is.Empty);
            Assert.That(callback.Detail, Is.EqualTo("Provider=Test; Error=user_not_registered; LoginName=stranger"));
        }

        [Test]
        public async Task Audit_StrictWriteFailureDoesNotSignIn()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var sink = new CapturingAuditSink();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], auditSink: sink);
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            //チャレンジと IdP までは通し、コールバックの直前から監査ログを書けなくする
            var challenge = await browser.GetAsync("/api/account/login/Test");
            var idpResponse = await browser.GetAsync(challenge.Headers.Location!.ToString());
            sink.Fail = true;
            try { await browser.GetAsync(idpResponse.Headers.Location!.ToString()); } catch (Exception) { /*TestServer は例外がそのまま届く*/ }

            Assert.That(browser.HasAuthCookie, Is.False, "記録できないログインは成立させない");
        }

        [Test]
        public async Task Audit_MobileSignInIsRecordedByTheTicketExchange()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var sink = new CapturingAuditSink();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], mobileCallback: "lowcodeapp://auth", auditSink: sink);
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            await browser.SignInThroughIdpAsync("/api/account/login/Test?mobile=true");

            Assert.That(sink.Events.Where(e => e.Action == "Account.ExternalLoginCallback"), Is.Empty, "ネイティブアプリの成立はチケット交換のアクションが記録する (二重に数えない)");
        }

        [Test]
        public async Task Entra_MultiTenant_UsesUpnAndRejectsGuests()
        {
            //organizations: issuer はテナント GUID 形式。discovery / token は偽 IdP が答える
            await using var idp = new FakeIdp { Issuer = "https://login.microsoftonline.com/11111111-2222-3333-4444-555555555555/v2.0" };
            idp.Claims["preferred_username"] = "guest_gmail.com#EXT#@tenant.onmicrosoft.com";
            idp.Claims["email"] = "guest@gmail.com";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new EntraLoginProvider(new() { ClientId = "client-1", ClientSecret = "s" })]);
            FakeResolver.Users["taro@example.co.jp"] = "U1";
            var browser = new Browser(app, idp);

            var guest = await browser.SignInThroughIdpAsync("/api/account/login/Entra");
            Assert.That(guest.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=guest_not_allowed"));
            Assert.That(idp.LastAuthorizeQuery["redirect_uri"], Is.EqualTo(AppBase + "/signin-entra"));

            idp.Claims["preferred_username"] = "taro@example.co.jp";
            idp.Claims["email"] = "spoofed@other.com";
            var ok = await browser.SignInThroughIdpAsync("/api/account/login/Entra");
            Assert.That(ok.Headers.Location!.ToString(), Is.EqualTo("/"), App.LastRemoteFailure);
            Assert.That(FakeResolver.Last!.LoginName, Is.EqualTo("taro@example.co.jp"), "email ではなく UPN");
            Assert.That(await (await browser.GetAsync("/api/account/current_user")).Content.ReadAsStringAsync(), Is.EqualTo("U1"));
        }

        [Test]
        public async Task Entra_MultiTenant_RejectsIssuerOfWrongShape()
        {
            await using var idp = new FakeIdp { Issuer = "https://login.microsoftonline.com/common/v2.0" };
            idp.Claims["preferred_username"] = "taro@example.co.jp";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new EntraLoginProvider(new() { ClientId = "client-1" })]);
            FakeResolver.Users["taro@example.co.jp"] = "U1";
            var browser = new Browser(app, idp);

            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Entra");
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=remote_failure"));
            Assert.That(browser.HasAuthCookie, Is.False);
            Assert.That(FakeResolver.Last, Is.Null, "トークン検証で落ちるので解決まで来ない");
        }

        [Test]
        public async Task Google_RequiresVerifiedEmail()
        {
            await using var idp = new FakeIdp { HasEndSession = false };
            idp.Claims["email"] = "taro@gmail.com";
            idp.Claims["email_verified"] = false;
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new GoogleLoginProvider(new() { ClientId = "client-1" })]);
            FakeResolver.Users["taro@gmail.com"] = "U1";
            var browser = new Browser(app, idp);

            var unverified = await browser.SignInThroughIdpAsync("/api/account/login/Google");
            Assert.That(unverified.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=invalid_claims"));

            idp.Claims["email_verified"] = true;
            var ok = await browser.SignInThroughIdpAsync("/api/account/login/Google");
            Assert.That(ok.Headers.Location!.ToString(), Is.EqualTo("/"), App.LastRemoteFailure);
            Assert.That(FakeResolver.Last!.LoginName, Is.EqualTo("taro@gmail.com"));

            //Google は end-session が無いのでローカルログアウトのみ
            var logout = await browser.PostAsync("/api/account/logout", new { });
            Assert.That((await logout.Content.ReadFromJsonAsync<LogoutResult>())!.Redirect, Is.Null);
            Assert.That(browser.HasAuthCookie, Is.False);
        }

        [Test]
        public async Task Cognito_LogoutGoesToHostedUiWithLogoutUri()
        {
            await using var idp = new FakeIdp { HasEndSession = false };
            idp.Claims["email"] = "taro@example.com";
            idp.Claims["email_verified"] = true;
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new CognitoLoginProvider(new() { ClientId = "client-1", Authority = "https://idp.test", Domain = "https://my.auth.ap-northeast-1.amazoncognito.com" })]);
            FakeResolver.Users["taro@example.com"] = "U1";
            var browser = new Browser(app, idp);

            var signIn = await browser.SignInThroughIdpAsync("/api/account/login/Cognito");
            Assert.That(signIn.Headers.Location!.ToString(), Is.EqualTo("/"), App.LastRemoteFailure);
            var logout = await browser.PostAsync("/api/account/logout", new { });
            var redirect = (await logout.Content.ReadFromJsonAsync<LogoutResult>())!.Redirect;
            Assert.That(redirect, Is.EqualTo("/api/account/logout/Cognito"));

            var response = await browser.GetAsync(redirect!);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            var q = Query(response.Headers.Location!.ToString());
            Assert.That(response.Headers.Location!.ToString(), Does.StartWith("https://my.auth.ap-northeast-1.amazoncognito.com/logout?"));
            Assert.That(q["client_id"], Is.EqualTo("client-1"));
            Assert.That(q["logout_uri"], Is.EqualTo(AppBase + "/login.html"));
            Assert.That(browser.HasAuthCookie, Is.False);
        }

        [Test]
        public async Task Mobile_ReturnsOneTimeTicketInsteadOfCookie()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], mobileCallback: "lowcodeapp://auth");
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            //システムブラウザ側: Cookie は発行されず、アプリのスキームへチケット付きで戻る
            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Test?mobile=true");
            Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            //Uri.ToString() は authority 形式のスキームに "/" を補うので生の値で見る
            var location = callback.Headers.Location!.OriginalString;
            Assert.That(location, Does.StartWith("lowcodeapp://auth?ticket="));
            Assert.That(browser.HasAuthCookie, Is.False);

            //アプリ側 (別の HttpClient = 別 Cookie): チケットを Cookie に交換
            var mobile = new Browser(app, idp);
            var ticket = location.Substring("lowcodeapp://auth?ticket=".Length);
            var exchange = await mobile.PostAsync("/api/account/login_ticket", new { Ticket = ticket });
            Assert.That(exchange.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(mobile.HasAuthCookie, Is.True);
            Assert.That(await (await mobile.GetAsync("/api/account/current_user")).Content.ReadAsStringAsync(), Is.EqualTo("U1"));

            //使い捨て
            var again = await new Browser(app, idp).PostAsync("/api/account/login_ticket", new { Ticket = ticket });
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            //モバイルのログアウトは Cookie 破棄のみ
            var logout = await mobile.PostAsync("/api/account/logout?mobile=true", new { });
            Assert.That((await logout.Content.ReadFromJsonAsync<LogoutResult>())!.Redirect, Is.Null);
            Assert.That(mobile.HasAuthCookie, Is.False);
        }

        [Test]
        public async Task Mobile_FailureGoesBackToAppWithError()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "stranger";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], mobileCallback: "lowcodeapp://auth");
            var browser = new Browser(app, idp);

            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Test?mobile=true");
            Assert.That(callback.Headers.Location!.OriginalString, Is.EqualTo("lowcodeapp://auth?error=user_not_registered"));
        }

        [Test]
        public async Task Mobile_NotConfigured_Is404()
        {
            await using var idp = new FakeIdp();
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })]);
            var browser = new Browser(app, idp);

            Assert.That((await browser.GetAsync("/api/account/login/Test?mobile=true")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await browser.GetAsync("/api/account/login/Unknown")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task NoProvidersConfigured_EndpointsStillWork()
        {
            await using var idp = new FakeIdp();
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, []);
            var browser = new Browser(app, idp);

            var options = await (await browser.GetAsync("/api/account/login_options")).Content.ReadFromJsonAsync<JsonElement>();
            Assert.That(options.GetProperty("providers").GetArrayLength(), Is.EqualTo(0));
            Assert.That((await browser.GetAsync("/api/account/login/Entra")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        //---- セッションの印 (ログイン済み Cookie の失効) -------------------------------------------

        /// <summary>ユーザー表 (SQLite)。契約は外部 IdP 専用 (パスワード列無し) + セッションの印の列。</summary>
        sealed class AccountDb : IAsyncDisposable
        {
            const string Ds = "Main";
            readonly string _file = Path.Combine(Path.GetTempPath(), $"extlogin_session_{Guid.NewGuid():N}.db");
            public DbAccessor Db { get; }
            public LoginAccountStore Accounts { get; }

            AccountDb()
            {
                DbAccessor.ClearTableDefinitionCache();
                Db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_file}" }]);
                var d = new DesignData();
                d.AppSettings.CurrentUserModuleDesignName = "AppUser";
                var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
                user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
                user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
                user.Fields.Add(new LoginAccountContractFieldDesign { Name = "LoginAccount", LoginName = "UserName", DbColumnSessionStamp = "session_stamp" });
                d.AddModule(user);
                Accounts = LoginAccountStore.Create(d, Db)!;
            }

            public static async Task<AccountDb> CreateAsync(params string[] userNames)
            {
                var db = new AccountDb();
                await db.Db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, user_name TEXT, session_stamp TEXT)", new());
                foreach (var name in userNames) await db.Db.ExecuteAsync(Ds, "INSERT INTO app_users (user_name) VALUES (@p1)", new() { { "@p1", name } });
                return db;
            }

            public async ValueTask DisposeAsync()
            {
                await Db.DisposeAsync();
                SqliteTestDb.Delete(_file);
            }
        }

        //T13: 外部 IdP のサインインで Cookie に印が積まれ、印を書き換える (全端末ログアウト) と次の要求から 401 になり Cookie も消える
        [Test]
        public async Task Session_ExternalSignInCarriesStamp_AndRevokeRejectsTheCookie()
        {
            await using var accounts = await AccountDb.CreateAsync("taro");
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], sessionAccounts: accounts.Accounts);
            FakeResolver.Accounts = accounts.Accounts;
            var browser = new Browser(app, idp);

            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Test");
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/"), App.LastRemoteFailure);
            Assert.That(await (await browser.GetAsync("/api/account/current_user")).Content.ReadAsStringAsync(), Is.EqualTo("1"));

            //Cookie の印 = 解決で採番した行の印
            var stamp = await (await browser.GetAsync("/api/account/session_stamp")).Content.ReadAsStringAsync();
            Assert.That(stamp, Is.Not.Empty);
            Assert.That(await accounts.Accounts.IsSessionValidAsync("1", stamp), Is.True);

            //別のブラウザ (別端末) も同じ印でログインしている
            var other = new Browser(app, idp);
            await other.SignInThroughIdpAsync("/api/account/login/Test");
            Assert.That(await (await other.GetAsync("/api/account/session_stamp")).Content.ReadAsStringAsync(), Is.EqualTo(stamp), "ログインのたびに印を変えない");

            await accounts.Accounts.RevokeSessionsAsync("1");
            var rejected = await browser.GetAsync("/api/account/current_user");
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(browser.HasAuthCookie, Is.False, "照合に落ちた Cookie は消す");
            Assert.That((await other.GetAsync("/api/account/current_user")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "全端末");

            //新しいログインは新しい印で通る
            await browser.SignInThroughIdpAsync("/api/account/login/Test");
            Assert.That((await browser.GetAsync("/api/account/current_user")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await (await browser.GetAsync("/api/account/session_stamp")).Content.ReadAsStringAsync(), Is.Not.EqualTo(stamp));
        }

        //T14: モバイルのチケットは本人確認した時点の印を持つ。発行後に失効させると、交換 (200) で得た Cookie は次の要求で 401
        [Test]
        public async Task Session_MobileTicketIssuedBeforeRevokeGivesCookieThatIsRejected()
        {
            await using var accounts = await AccountDb.CreateAsync("taro");
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], mobileCallback: "lowcodeapp://auth", sessionAccounts: accounts.Accounts);
            FakeResolver.Accounts = accounts.Accounts;

            async Task<string> TicketAsync()
            {
                var callback = await new Browser(app, idp).SignInThroughIdpAsync("/api/account/login/Test?mobile=true");
                var location = callback.Headers.Location!.OriginalString;
                Assert.That(location, Does.StartWith("lowcodeapp://auth?ticket="), App.LastRemoteFailure);
                return location.Substring("lowcodeapp://auth?ticket=".Length);
            }

            //失効しなければチケットの Cookie は使える (印も積まれる)
            var ok = new Browser(app, idp);
            Assert.That((await ok.PostAsync("/api/account/login_ticket", new { Ticket = await TicketAsync() })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await ok.GetAsync("/api/account/current_user")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await (await ok.GetAsync("/api/account/session_stamp")).Content.ReadAsStringAsync(), Is.Not.Empty);

            //チケット発行後に失効 → 交換は通る (交換時に最新を読み直さない) が、その Cookie は次の要求で落ちる
            var ticket = await TicketAsync();
            await accounts.Accounts.RevokeSessionsAsync("1");
            var mobile = new Browser(app, idp);
            Assert.That((await mobile.PostAsync("/api/account/login_ticket", new { Ticket = ticket })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await mobile.GetAsync("/api/account/current_user")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(mobile.HasAuthCookie, Is.False);
            Assert.That((await ok.GetAsync("/api/account/current_user")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "先に交換した端末も落ちる");
        }

        //T16: nonce が合わない応答では Cookie もモバイルのチケットも出さない (発行は nonce 等の検証が終わった OnTicketReceived)
        [Test]
        public async Task NonceMismatch_IssuesNeitherCookieNorMobileTicket()
        {
            await using var idp = new FakeIdp { NonceOverride = "forged-nonce" };
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var sink = new CapturingAuditSink();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], mobileCallback: "lowcodeapp://auth", auditSink: sink);
            FakeResolver.Users["taro"] = "U1";

            var web = new Browser(app, idp);
            var callback = await web.SignInThroughIdpAsync("/api/account/login/Test");
            Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=remote_failure"));
            Assert.That(web.HasAuthCookie, Is.False);
            Assert.That(FakeResolver.Last, Is.Not.Null, "id_token の検証は通るので解決までは来る (nonce の検証はその後)");
            Assert.That(sink.Events.Where(e => e.Action == "Account.ExternalLoginCallback").Select(e => e.Result), Is.EqualTo(new[] { AuditResult.Denied }), "成立は記録しない");

            var mobile = new Browser(app, idp);
            var mobileCallback = await mobile.SignInThroughIdpAsync("/api/account/login/Test?mobile=true");
            Assert.That(mobileCallback.Headers.Location!.OriginalString, Is.EqualTo("lowcodeapp://auth?error=remote_failure"));
            Assert.That(mobile.HasAuthCookie, Is.False);
        }

        /// <summary>書き込みが失敗するキャッシュ (共有キャッシュの障害)。モバイルのチケットを発行できない。</summary>
        sealed class FailingCache : IDistributedCache
        {
            public byte[]? Get(string key) => null;
            public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
            public void Refresh(string key) { }
            public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
            public void Remove(string key) { }
            public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
            public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new IOException("cache down");
            public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new IOException("cache down");
        }

        //OnTicketReceived は認証ハンドラの try の外。そこで失敗しても (チケットの置き場の障害) 500 にせず、拒否として監査に残してアプリへ差し戻す
        [Test]
        public async Task Mobile_TicketIssueFailureGoesBackToAppWithError()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var sink = new CapturingAuditSink();
            await using var app = new App();
            await app.StartAsync(idp, [new OidcLoginProvider(new() { Name = "Test", ClientId = "client-1", Authority = "https://idp.test" })], mobileCallback: "lowcodeapp://auth",
                auditSink: sink, cache: new FailingCache());
            FakeResolver.Users["taro"] = "U1";
            var browser = new Browser(app, idp);

            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Test?mobile=true");
            Assert.That(callback.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(callback.Headers.Location!.OriginalString, Is.EqualTo("lowcodeapp://auth?error=remote_failure"));
            Assert.That(browser.HasAuthCookie, Is.False);
            Assert.That(FakeResolver.Last, Is.Not.Null, "解決までは済んでいる");
            var denied = sink.Events.Single(e => e.Action == "Account.ExternalLoginCallback");
            Assert.That(denied.Result, Is.EqualTo(AuditResult.Denied));
            Assert.That(denied.Detail, Does.StartWith("Provider=Test; Error=remote_failure; ").And.Contain("cache down"));
        }

        /// <summary>Configure で OpenID Connect のイベントに独自処理を入れるプロバイダ (独自 IdP の継承の形)。</summary>
        sealed class HookingProvider : OidcLoginProvider
        {
            public HookingProvider() : base(new OidcLoginSettings { Name = "Hook", ClientId = "client-1", Authority = "https://idp.test" }) { }
            public List<string> Log { get; } = new();
            public bool HandleOnTokenValidated { get; set; }
            public bool HandleOnTicketReceived { get; set; }

            public override void Configure(OpenIdConnectOptions options)
            {
                base.Configure(options);
                options.Events.OnTokenValidated = ctx =>
                {
                    //共通処理 (ユーザー解決) より前: まだ IdP のプリンシパルで、解決も呼ばれていない
                    Log.Add($"provider:TokenValidated user={LoginPrincipal.GetUserId(ctx.Principal)} resolved={FakeResolver.Last != null}");
                    if (HandleOnTokenValidated) { ctx.Response.Redirect("/login.html?error=custom"); ctx.HandleResponse(); }
                    return Task.CompletedTask;
                };
                options.Events.OnTicketReceived = ctx =>
                {
                    //共通の OnTokenValidated の後: プリンシパルはアプリのユーザーに差し替え済み
                    Log.Add($"provider:TicketReceived user={LoginPrincipal.GetUserId(ctx.Principal)}");
                    if (HandleOnTicketReceived) { ctx.Response.Redirect("/login.html?error=custom"); ctx.HandleResponse(); }
                    return Task.CompletedTask;
                };
            }
        }

        //T17: プロバイダが Configure でイベントを設定しても共通処理は後ろで動く (順: プロバイダ → 共通)。プロバイダが応答を処理したら共通処理は走らない
        [Test]
        public async Task ProviderEvents_RunBeforeCommonHandling()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var provider = new HookingProvider();
            await using var app = new App();
            await app.StartAsync(idp, [provider]);
            FakeResolver.Users["taro"] = "U1";

            var browser = new Browser(app, idp);
            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Hook");
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/"), App.LastRemoteFailure);
            Assert.That(browser.HasAuthCookie, Is.True, "共通処理が Cookie を発行する");
            Assert.That(await (await browser.GetAsync("/api/account/current_user")).Content.ReadAsStringAsync(), Is.EqualTo("U1"));
            Assert.That(provider.Log, Is.EqualTo(new[] { "provider:TokenValidated user= resolved=False", "provider:TicketReceived user=U1" }));
        }

        [Test]
        public async Task ProviderEvents_HandledByProvider_SkipsCommonHandling()
        {
            await using var idp = new FakeIdp();
            idp.Claims["preferred_username"] = "taro";
            await idp.StartAsync();
            var provider = new HookingProvider { HandleOnTokenValidated = true };
            await using var app = new App();
            await app.StartAsync(idp, [provider]);
            FakeResolver.Users["taro"] = "U1";

            //OnTokenValidated で処理済み: 解決も Cookie も無い
            var browser = new Browser(app, idp);
            var callback = await browser.SignInThroughIdpAsync("/api/account/login/Hook");
            Assert.That(callback.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=custom"));
            Assert.That(browser.HasAuthCookie, Is.False);
            Assert.That(FakeResolver.Last, Is.Null, "共通のユーザー解決は走らない");

            //OnTicketReceived で処理済み: 解決は済んでいるが Cookie は出ない
            provider.HandleOnTokenValidated = false;
            provider.HandleOnTicketReceived = true;
            var second = new Browser(app, idp);
            var callback2 = await second.SignInThroughIdpAsync("/api/account/login/Hook");
            Assert.That(callback2.Headers.Location!.ToString(), Is.EqualTo("/login.html?error=custom"));
            Assert.That(second.HasAuthCookie, Is.False);
            Assert.That(FakeResolver.Last, Is.Not.Null);
        }
    }
}
