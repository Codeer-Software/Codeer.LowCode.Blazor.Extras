using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class MailService
    {
        [ScriptInject]
        public IHttpService? Http { get; set; }

        /// <summary>
        /// Mail endpoint. URLs belong to the app (which owns the controllers),
        /// so set this once at startup (e.g. in ServiceInitializer). Not used when <see cref="SendMailAsyncCore"/> is set.
        /// </summary>
        [ScriptHide]
        public static string SendMailEndPoint { get; set; } = string.Empty;

        /// <summary>
        /// Host-side hook. When set (e.g. desktop apps), mails are sent directly
        /// without going through the <see cref="SendMailEndPoint"/>.
        /// </summary>
        [ScriptHide]
        public static Func<MailMessage, Task<bool>>? SendMailAsyncCore { get; set; }

        [ScriptName("CreateMessage")]
        public MailMessage CreateMessage() => new();

        [ScriptName("SendEmail")]
        public async Task<bool> SendEmailAsync(string address, string subject, string message)
            => await SendAsync(new MailMessage().AddTo(address).SetSubject(subject).SetBody(message));

        [ScriptName("Send")]
        public async Task<bool> SendAsync(MailMessage message)
        {
            if (SendMailAsyncCore != null) return await SendMailAsyncCore(message);
            var endPoint = SendMailEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return false;
            var ret = await Http.PostAsJsonAsync<MailMessage, ValueWrapper<bool>>(endPoint, message);
            return ret?.Value ?? false;
        }
    }
}
