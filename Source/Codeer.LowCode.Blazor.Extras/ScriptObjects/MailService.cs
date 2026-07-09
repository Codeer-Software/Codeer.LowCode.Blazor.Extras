using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class MailService
    {
        [ScriptInject]
        public IHttpService? Http { get; set; }

        [ScriptInject]
        public ExtrasClientOptions? Options { get; set; }

        /// <summary>
        /// Host-side hook. When set (e.g. desktop apps), mails are sent directly
        /// without going through the mail endpoint.
        /// </summary>
        [ScriptHide]
        public static Func<MailMessage, Task<bool>>? SendMailAsyncCore { get; set; }

        [ScriptName("CreateMessage")]
        public virtual MailMessage CreateMessage() => new();

        [ScriptName("SendEmail")]
        public virtual async Task<bool> SendEmailAsync(string address, string subject, string message)
            => await SendAsync(new MailMessage().AddTo(address).SetSubject(subject).SetBody(message));

        [ScriptName("Send")]
        public virtual async Task<bool> SendAsync(MailMessage message)
        {
            if (SendMailAsyncCore != null) return await SendMailAsyncCore(message);
            var endPoint = Options?.MailEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return false;
            var ret = await Http.PostAsJsonAsync<MailMessage, ValueWrapper<bool>>(endPoint, message);
            return ret?.Value ?? false;
        }
    }
}
