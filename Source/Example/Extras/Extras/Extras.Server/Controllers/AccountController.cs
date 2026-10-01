using System.Security.Claims;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.SystemSettings;
using Extras.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Extras.Server.Controllers
{
    public class DemoUser
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class DemoLoginRequest
    {
        public string UserId { get; set; } = string.Empty;
    }

    //デモ用の簡易ログイン (パスワードなしのユーザー切替)。承認フロー等の
    //「操作ユーザーが必要な機能」をサンプルで確認するためのもので、実運用の認証ではない。
    //実運用は Cookie / AAD バリアントのテンプレートを使うこと
    [ApiController]
    [Route("api/account")]
    public class AccountController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;

        public AccountController(DataService dataService)
        {
            _dataService = dataService;
        }

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        [HttpGet("users")]
        public async Task<List<DemoUser>> GetUsersAsync()
        {
            var ds = SystemConfig.Instance.DataSources.First().Name;
            var rows = await _dataService.DbAccess.QueryAsync(ds, "select id, name from app_users order by id", new());
            return rows.Select(e => new DemoUser
            {
                Id = e.Values.ElementAt(0)?.ToString() ?? string.Empty,
                Name = e.Values.ElementAt(1)?.ToString() ?? string.Empty,
            }).ToList();
        }

        //監査ログ: ログインは認証前なのでユーザーが Cookie から取れない。試行したユーザーと、成功したユーザー Id を AuditContext から足す
        [HttpPost("login"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> LoginAsync(DemoLoginRequest request, [FromServices] AuditContext audit)
        {
            audit.Event.Detail = $"LoginName={request.UserId}";
            var user = (await GetUsersAsync()).FirstOrDefault(e => e.Id == request.UserId);
            if (user == null)
            {
                audit.Deny("unknown user");
                return BadRequest("unknown user");
            }
            audit.Event.UserId = user.Id;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.Name),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Ok();
        }

        [HttpPost("logout"), Audit(AuditCategory.Authentication)]
        public async Task<IActionResult> LogoutAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
        }

        [HttpGet("current")]
        public DemoUser GetCurrent() => new()
        {
            Id = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            Name = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
        };
    }
}
