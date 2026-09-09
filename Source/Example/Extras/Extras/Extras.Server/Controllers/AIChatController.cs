using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Extras.Server.AI;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Extras.Server.Controllers
{
    /// <summary>
    /// AIChatField の窓口。送信は即 requestId を返し (202)、クライアントは GET でポーリングする。
    /// 返事を作る Agent は AIChatAgentTable (Agent 名 → Agent の対応表) で選ばれる。AIChatField のデザインの Agent 名が鍵。
    /// </summary>
    [ApiController]
    [Route("api/ai_chat")]
    public class AIChatController : ControllerBase
    {
        //ジョブ置き場と Agent の対応表はアプリの静的な持ち物 (AI/AIChatAgentTable.cs)。メールの MailSenderTable と同じ位置づけ
        static AIChatJobStore _jobs => AIChatAgentTable.Jobs;

        //ジョブの所有者。他人のジョブは見えない。表示名は同名・改名がありうるのでユーザー ID を優先する。
        //匿名同士は共有になるので、テンプレートに持っていくときは [Authorize] を付けて匿名で入れないようにする
        string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? string.Empty;

        [HttpPost]
        public ActionResult<AIChatSendResponse> Send([FromBody] AIChatSendRequest request)
            => Accepted(new AIChatSendResponse { RequestId = _jobs.Start(Owner, request.ConversationId, request.Message, request.Agent) });

        [HttpGet("{requestId}")]
        public ActionResult<AIChatStatusResponse> Status(string requestId)
        {
            var status = _jobs.GetStatus(Owner, requestId);
            return status == null ? NotFound() : status;
        }

        [HttpDelete("{requestId}")]
        public IActionResult Cancel(string requestId)
            => _jobs.Cancel(Owner, requestId) ? NoContent() : NotFound();
    }
}
