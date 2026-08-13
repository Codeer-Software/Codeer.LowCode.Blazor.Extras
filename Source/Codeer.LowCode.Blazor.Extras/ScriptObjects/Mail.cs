using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// Single mail built and sent from scripts:
    /// <code>
    /// var mail = new Mail();
    /// mail.Sender = "Notify";           //Mail.Senders の名前(省略時は DefaultSenderName、無ければ先頭)
    /// mail.AddTo("a@example.com");
    /// mail.Subject = "件名";
    /// mail.Body = "本文";
    /// mail.Source = this;               //送信履歴に元レコードを記録(省略可)
    /// var result = mail.Send();
    /// </code>
    /// </summary>
    public class Mail
    {
        [ScriptHide, ScriptInject]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        [ScriptHide, ScriptInject]
        public IHttpService? Http { get; set; }

        /// <summary>Sender name configured in appsettings (Mail.Senders). Empty = Mail.DefaultSenderName (then the first sender).</summary>
        public string Sender { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>Record this send originates from. Recorded as SourceModule/SourceId in the send history.</summary>
        public Module? Source { get; set; }

        readonly MailMessage _message = new();

        [ScriptName("AddTo")]
        public Mail AddTo(string address)
        {
            _message.To.AddRange(Split(address));
            return this;
        }

        [ScriptName("AddCc")]
        public Mail AddCc(string address)
        {
            _message.Cc.AddRange(Split(address));
            return this;
        }

        [ScriptName("AddBcc")]
        public Mail AddBcc(string address)
        {
            _message.Bcc.AddRange(Split(address));
            return this;
        }

        static IEnumerable<string> Split(string address)
            => address.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e));

        [ScriptName("AddAttachment")]
        public Mail AddAttachment(string fileName, Excel excel)
        {
            _message.Attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(excel.GetBytes()) });
            return this;
        }

        [ScriptName("AddTextAttachment")]
        public Mail AddTextAttachment(string fileName, string text)
        {
            _message.Attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) });
            return this;
        }

        [ScriptName("Send")]
        public async Task<MailSendResult> SendAsync()
        {
            _message.Subject = Subject;
            _message.Body = Body;
            _message.IsBodyHtml = IsBodyHtml;
            _message.ReplyTo = ReplyTo;
            var request = new MailSendRequest
            {
                SenderName = Sender,
                Message = _message,
                SourceModule = Source?.Design.Name ?? string.Empty,
                SourceId = Source?.GetIdText() ?? string.Empty,
            };
            var result = await MailTransport.SendAsync(Http, request);
            await LogFailuresAsync(Services, result);
            return result;
        }

        //スクリプトが戻り値を見ていなくても失敗を追えるようにログへ流す
        internal static async Task LogFailuresAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services? services, MailSendResult result)
        {
            if (result.IsSuccess || services == null) return;
            var detail = string.Join(" / ", result.Failures.Take(5).Select(e => string.IsNullOrEmpty(e.To) ? e.Error : $"{e.To}: {e.Error}"));
            await services.Logger.Error($"Mail send failed ({result.Failures.Count}/{result.TotalCount}): {detail}");
        }
    }
}
