using System.Net.Mail;
using MailMessage = Codeer.LowCode.Blazor.Extras.ScriptObjects.MailMessage;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    public class SmtpMailService
    {
        readonly MailSettings _settings;

        public SmtpMailService(MailSettings settings) => _settings = settings;

        public virtual async Task<bool> SendAsync(MailMessage message)
        {
            if (string.IsNullOrEmpty(_settings.Host)) return false;
            if (!int.TryParse(_settings.Port, out var port)) return false;
            if (!message.To.Any()) return false;

            using var mailer = new SmtpClient(_settings.Host, port);
            mailer.Credentials = new System.Net.NetworkCredential(_settings.SenderMailAddress, _settings.Password);

            if (bool.TryParse(_settings.SSL, out var ssl) && ssl)
            {
                mailer.EnableSsl = true;
            }

            var from = string.IsNullOrEmpty(_settings.SenderDisplayName)
                ? new MailAddress(_settings.SenderMailAddress)
                : new MailAddress(_settings.SenderMailAddress, _settings.SenderDisplayName);

            using var msg = new System.Net.Mail.MailMessage();
            msg.Sender = from;
            msg.From = from;
            foreach (var e in message.To) msg.To.Add(new MailAddress(e));
            foreach (var e in message.Cc) msg.CC.Add(new MailAddress(e));
            foreach (var e in message.Bcc) msg.Bcc.Add(new MailAddress(e));
            if (!string.IsNullOrEmpty(message.ReplyTo)) msg.ReplyToList.Add(new MailAddress(message.ReplyTo));
            msg.Subject = message.Subject;
            msg.Body = message.Body;
            msg.IsBodyHtml = message.IsBodyHtml;

            var streams = new List<MemoryStream>();
            try
            {
                foreach (var e in message.Attachments)
                {
                    var stream = new MemoryStream(Convert.FromBase64String(e.ContentBase64));
                    streams.Add(stream);
                    msg.Attachments.Add(new Attachment(stream, e.FileName));
                }
                await mailer.SendMailAsync(msg);
            }
            finally
            {
                streams.ForEach(e => e.Dispose());
            }
            return true;
        }
    }
}
