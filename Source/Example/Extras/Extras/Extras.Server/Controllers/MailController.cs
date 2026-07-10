using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Utils;
using Extras.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Extras.Server.Controllers
{
    [ApiController]
    [Route("api/mail")]
    public class MailController : ControllerBase
    {
        [HttpPost]
        public async Task<ValueWrapper<bool>> SendEmailAsync(MailMessage request)
            => new(await new SmtpMailService(SystemConfig.Instance.MailSettings).SendAsync(request));
    }
}
