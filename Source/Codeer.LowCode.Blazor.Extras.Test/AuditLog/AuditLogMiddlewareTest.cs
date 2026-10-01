using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    /// <summary>ミドルウェアのテスト用 API (MVC が見つけられるようトップレベルに置く)。</summary>
    [ApiController, Route("api/probe")]
    public class AuditProbeController : ControllerBase
    {
        public static int WriteCalls;

        readonly AuditContext _audit;
        public AuditProbeController(AuditContext audit) => _audit = audit;

        [HttpGet("write"), Audit(AuditCategory.DataWrite)]
        public IActionResult Write()
        {
            WriteCalls++;
            _audit.AddTarget("Customer", "7", "Update");
            return Ok("done");
        }

        [HttpGet("read"), Audit(AuditCategory.DataRead)]
        public IActionResult Read() => Ok();

        [HttpGet("plain")]
        public IActionResult Plain() => Ok();

        [HttpGet("throw"), Audit(AuditCategory.DataWrite)]
        public IActionResult Throw() => throw new InvalidOperationException("boom");

        [HttpGet("fail"), Audit(AuditCategory.DataWrite)]
        public IActionResult Fail()
        {
            _audit.Fail("save error");
            return Ok();
        }

        [HttpGet("login"), Audit(AuditCategory.Authentication)]
        public IActionResult Login(string? user)
        {
            _audit.Event.Detail = $"LoginName={user}";
            if (user != "alice") return Unauthorized();
            _audit.Event.UserId = "user-alice";
            return Ok();
        }

        [HttpGet("secret"), Authorize, Audit(AuditCategory.DataRead)]
        public IActionResult Secret() => Ok("secret");
    }

    /// <summary>
    /// ミドルウェアを TestServer で通す。二段の記録 (試行 → 結果)、認可ミドルウェアの 401、アクションの例外、
    /// コントローラからの上書き (対象・失敗・ユーザー)、Strict で書けないときの挙動、無効時の素通しを確認する。
    /// ミドルウェアは認証ミドルウェアより前に置く (本番と同じ並び)。
    /// </summary>
    public class AuditLogMiddlewareTest
    {
        /// <summary>X-User ヘッダがあればそのユーザー、無ければ 401。</summary>
        sealed class HeaderAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var user = Request.Headers["X-User"].ToString();
                if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
            }
        }

        sealed class App : IAsyncDisposable
        {
            WebApplication? _app;
            public CapturingAuditSink Sink { get; } = new();
            public HttpClient Client => _app!.GetTestClient();

            public async Task StartAsync(AuditLogSettings settings, bool clearStartEvents = true)
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.UseTestServer();
                builder.Logging.ClearProviders();
                builder.Services.AddAuthentication("Header").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Header", null);
                builder.Services.AddAuthorization();
                builder.Services.AddControllers().AddApplicationPart(typeof(AuditProbeController).Assembly);
                builder.Services.AddAuditLog(settings, [Sink]);

                _app = builder.Build();
                _app.UseExceptionHandler(e => e.Run(async ctx => { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsync("error"); }));
                _app.UseRouting();
                _app.UseAuditLog();
                _app.UseAuthentication();
                _app.UseAuthorization();
                _app.MapControllers();
                await _app.StartAsync();
                if (clearStartEvents) Sink.Events.Clear(); //Application.Start / 掃除の記録は見ない
            }

            public async ValueTask DisposeAsync()
            {
                if (_app == null) return;
                await _app.StopAsync(); //本番の app.Run() の停止と同じ経路 (Dispose だけでは HostedService の StopAsync は走らない)
                await _app.DisposeAsync();
            }
        }

        static async Task<HttpResponseMessage> GetAsync(App app, string path, string? user = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (user != null) request.Headers.Add("X-User", user);
            request.Headers.UserAgent.ParseAdd("probe/1.0");
            return await app.Client.SendAsync(request);
        }

        [Test]
        public async Task WriteIsRecordedInTwoPhases_AttemptThenResult()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true });

            var response = await GetAsync(app, "/api/probe/write", user: "u1");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(app.Sink.Events, Has.Count.EqualTo(2));
            var attempt = app.Sink.Events[0];
            var result = app.Sink.Events[1];
            Assert.That(attempt.Result, Is.EqualTo(AuditResult.Attempt));
            Assert.That(attempt.Action, Is.EqualTo("AuditProbe.Write"));
            Assert.That(attempt.Category, Is.EqualTo(AuditCategory.DataWrite));
            Assert.That(attempt.UserId, Is.EqualTo("u1"), "前段でもユーザーが入る (ミドルウェアが認証を解決する)");
            Assert.That(attempt.Targets, Is.Empty);
            Assert.That(result.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(result.Action, Is.EqualTo("AuditProbe.Write"));
            Assert.That(result.UserId, Is.EqualTo("u1"));
            Assert.That(result.UserAgent, Is.EqualTo("probe/1.0"));
            Assert.That(result.RequestId, Is.Not.Empty.And.EqualTo(attempt.RequestId), "2 行は RequestId で結ぶ");
            Assert.That(result.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Customer", "7", "Update") }));
        }

        [Test]
        public async Task ReadHasNoAttemptRow()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true });
            await GetAsync(app, "/api/probe/read", user: "u1");
            Assert.That(app.Sink.Events.Select(e => e.Result).ToArray(), Is.EqualTo(new[] { AuditResult.Success }));
        }

        [Test]
        public async Task AttemptCategoriesCanBeTurnedOff()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true, AttemptCategories = [] });
            await GetAsync(app, "/api/probe/write", user: "u1");
            Assert.That(app.Sink.Events.Select(e => e.Result).ToArray(), Is.EqualTo(new[] { AuditResult.Success }));
        }

        [Test]
        public async Task UnattributedActionIsOther_WithoutAttempt()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true });
            await GetAsync(app, "/api/probe/plain");
            var e = app.Sink.Events.Single();
            Assert.That(e.Category, Is.EqualTo(AuditCategory.Other));
            Assert.That(e.Result, Is.EqualTo(AuditResult.Success));
        }

        [Test]
        public async Task AuthorizationDenialIsRecordedAsDenied()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true, Categories = [AuditCategory.DataWrite] });

            var denied = await GetAsync(app, "/api/probe/secret");
            var allowed = await GetAsync(app, "/api/probe/secret", user: "u1");

            Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            //DataRead は Categories に無いので成功は記録されず、拒否だけが残る
            var e = app.Sink.Events.Single();
            Assert.That(e.Action, Is.EqualTo("AuditProbe.Secret"));
            Assert.That(e.Result, Is.EqualTo(AuditResult.Denied));
        }

        [Test]
        public async Task ExceptionIsRecordedAsFailure_AfterAttempt()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true });

            var response = await GetAsync(app, "/api/probe/throw", user: "u1");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(app.Sink.Events.Select(e => e.Result).ToArray(), Is.EqualTo(new[] { AuditResult.Attempt, AuditResult.Failure }));
            var e = app.Sink.Events[1];
            Assert.That(e.Detail, Is.EqualTo("boom"));
            Assert.That(e.UserId, Is.EqualTo("u1"));
        }

        [Test]
        public async Task ControllerCanMarkBusinessFailure()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true });
            var response = await GetAsync(app, "/api/probe/fail");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var e = app.Sink.Events.Last();
            Assert.That(e.Result, Is.EqualTo(AuditResult.Failure));
            Assert.That(e.Detail, Is.EqualTo("save error"));
        }

        [Test]
        public async Task LoginRecordsAttemptedNameAndSignedInUser()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true });

            await GetAsync(app, "/api/probe/login?user=mallory");
            await GetAsync(app, "/api/probe/login?user=alice");

            Assert.That(app.Sink.Events.Select(e => (e.Result, e.UserId, e.Detail)).ToArray(), Is.EqualTo(new[]
            {
                (AuditResult.Attempt, "", ""),
                (AuditResult.Denied, "", "LoginName=mallory"),
                (AuditResult.Attempt, "", ""),
                (AuditResult.Success, "user-alice", "LoginName=alice"),
            }));
        }

        [Test]
        public async Task StrictAttemptFailureBlocksTheOperation()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true, FailureMode = AuditFailureMode.Strict });
            app.Sink.Fail = true;
            var before = AuditProbeController.WriteCalls;

            //前段はアクションの前に書くので、書けなければアクションは実行されず、例外ハンドラが 500 を返す
            var response = await GetAsync(app, "/api/probe/write", user: "u1");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("error"));
            Assert.That(AuditProbeController.WriteCalls, Is.EqualTo(before), "操作は実行されない");
        }

        [Test]
        public async Task StrictResultFailureFailsTheResponse()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true, FailureMode = AuditFailureMode.Strict, AttemptCategories = [] });
            app.Sink.Fail = true;

            //後段はレスポンスの先頭が出る前 (OnStarting) に走り、失敗はそこで例外になる。
            //Kestrel / IIS はこの時点ではレスポンス未開始なので例外ハンドラが 500 を返す。TestServer は開始済み扱いにするため例外がそのまま届く。
            //どちらでも「200 と本文は返らない」が契約
            var ex = Assert.ThrowsAsync<AuditLogException>(() => GetAsync(app, "/api/probe/write", user: "u1"));
            Assert.That(ex!.InnerException, Is.InstanceOf<IOException>());
        }

        [Test]
        public async Task BestEffortSinkFailureKeepsTheResponse()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true, FailureMode = AuditFailureMode.BestEffort });
            app.Sink.Fail = true;

            var response = await GetAsync(app, "/api/probe/write", user: "u1");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("done"));
        }

        [Test]
        public async Task DisabledPassesThrough()
        {
            await using var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = false });
            var response = await GetAsync(app, "/api/probe/write", user: "u1");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(app.Sink.Events, Is.Empty);
        }

        [Test]
        public async Task StartPurgeAndStopAreRecordedAsSystemEvents()
        {
            var app = new App();
            await app.StartAsync(new AuditLogSettings { Enabled = true, RetentionDays = 7 }, clearStartEvents: false);
            Assert.That(app.Sink.Events.Select(e => (e.Category, e.Action)).ToArray(), Is.EqualTo(new[]
            {
                (AuditCategory.System, "Application.Start"),
                (AuditCategory.System, "AuditLog.Purge"),
            }));
            Assert.That(app.Sink.Events[0].Detail, Does.Contain("RetentionDays=7").And.Contain("AttemptCategories=Authentication,DataWrite,Export,Admin").And.Contain("Sinks=CapturingAuditSink"));
            await app.DisposeAsync();
            Assert.That(app.Sink.Events.Last().Action, Is.EqualTo("Application.Stop"));
        }
    }
}
