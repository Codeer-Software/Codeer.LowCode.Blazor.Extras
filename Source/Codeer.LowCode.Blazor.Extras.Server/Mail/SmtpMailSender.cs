using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
//ScriptObjects には旧 API (0.5.0 互換) の MailMessage / MailAttachment もあるので、こちらは新 API の型を使う
using MailAttachment = Codeer.LowCode.Blazor.Extras.Mail.MailAttachment;
using MailMessage = Codeer.LowCode.Blazor.Extras.Mail.MailMessage;
using MailKit.Security;
using MimeKit;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// <see cref="IMailSender"/> の SMTP 実装 (MailKit)。
    /// 一斉送信は宛先ごとにテンプレートを解決し、1つの接続で逐次送信する。
    /// </summary>
    public class SmtpMailSender : IMailSender
    {
        readonly SmtpSettings _settings;

        public SmtpMailSender(SmtpSettings settings) => _settings = settings;

        public int MaxBulkCount => _settings.MaxBulkCount;

        public async Task<MailSendResult> SendAsync(MailMessage message)
        {
            if (!TryGetConnectionInfo(out var port, out var error))
                return MailSendResult.Failure(message.To.FirstOrDefault() ?? string.Empty, error);

            using var client = new MailKit.Net.Smtp.SmtpClient();
            try
            {
                await ConnectAsync(client, port);
                await client.SendAsync(CreateMimeMessage(_settings.SenderMailAddress, _settings.SenderDisplayName, message));
                return MailSendResult.Success(1);
            }
            catch (Exception ex)
            {
                return MailSendResult.Failure(string.Join(";", message.To), ex.Message);
            }
            finally
            {
                if (client.IsConnected) await client.DisconnectAsync(true);
            }
        }

        public async Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
        {
            var result = new MailSendResult { TotalCount = recipients.Count };
            if (!recipients.Any()) return result;
            if (!TryGetConnectionInfo(out var port, out var configError))
            {
                result.Failures.AddRange(recipients.Select(e => new MailSendFailure { To = e.To, Error = configError }));
                return result;
            }

            using var client = new MailKit.Net.Smtp.SmtpClient();
            try
            {
                await ConnectAsync(client, port);
                foreach (var recipient in recipients)
                {
                    try
                    {
                        await client.SendAsync(CreateMimeMessage(_settings.SenderMailAddress, _settings.SenderDisplayName,
                            CreateResolvedMessage(template, recipient)));
                        result.SuccessCount++;
                    }
                    catch (Exception ex)
                    {
                        result.Failures.Add(new MailSendFailure { To = recipient.To, Error = ex.Message });
                    }
                }
            }
            catch (Exception ex)
            {
                //connect/auth failure - everything not yet sent fails
                var sentOrFailed = result.SuccessCount + result.Failures.Count;
                result.Failures.AddRange(recipients.Skip(sentOrFailed).Select(e => new MailSendFailure { To = e.To, Error = ex.Message }));
            }
            finally
            {
                if (client.IsConnected) await client.DisconnectAsync(true);
            }
            return result;
        }

        /// <summary>
        /// 一斉送信テンプレート+宛先1件を通常のメッセージに解決する
        /// (ネイティブ一斉送信 API を持たない実装の逐次送信用。使い方は GraphApiMailSender 参照)。
        /// </summary>
        internal static MailMessage CreateResolvedMessage(MailBulkTemplate template, MailBulkRecipient recipient)
            => new()
            {
                From = template.From,
                FromDisplayName = template.FromDisplayName,
                To = { recipient.To },
                Cc = recipient.Cc.ToList(),
                Bcc = recipient.Bcc.ToList(),
                Subject = MailTemplateEngine.Fill(template.Subject, recipient.Variables),
                Body = MailTemplateEngine.Fill(template.Body, recipient.Variables),
                IsBodyHtml = template.IsBodyHtml,
                ReplyTo = template.ReplyTo,
                Attachments = template.Attachments.ToList(),
            };

        bool TryGetConnectionInfo(out int port, out string error)
        {
            port = 0;
            error = string.Empty;
            if (string.IsNullOrEmpty(_settings.Host)) error = "SMTP host is not configured.";
            else if (!int.TryParse(_settings.Port, out port)) error = "SMTP port is not configured.";
            return string.IsNullOrEmpty(error);
        }

        async Task ConnectAsync(MailKit.Net.Smtp.SmtpClient client, int port)
        {
            var ssl = bool.TryParse(_settings.SSL, out var s) && s;
            //465 is implicit TLS by convention; other ports with SSL=true use STARTTLS
            var options = ssl
                ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
                : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(_settings.Host, port, options);
            if (!string.IsNullOrEmpty(_settings.Password))
            {
                var user = string.IsNullOrEmpty(_settings.UserName) ? _settings.SenderMailAddress : _settings.UserName;
                await client.AuthenticateAsync(user, _settings.Password);
            }
        }

        internal static MimeMessage CreateMimeMessage(string senderMailAddress, string senderDisplayName, MailMessage message)
        {
            var mime = new MimeMessage();
            //From はサーバーが解決した本人のアドレスのみ (MailDispatcher が保証)。空ならプロバイダ設定の差出人
            mime.From.Add(string.IsNullOrEmpty(message.From)
                ? new MailboxAddress(senderDisplayName, senderMailAddress)
                : new MailboxAddress(message.FromDisplayName, message.From));
            foreach (var e in message.To) mime.To.Add(MailboxAddress.Parse(e));
            foreach (var e in message.Cc) mime.Cc.Add(MailboxAddress.Parse(e));
            foreach (var e in message.Bcc) mime.Bcc.Add(MailboxAddress.Parse(e));
            if (!string.IsNullOrEmpty(message.ReplyTo)) mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
            mime.Subject = message.Subject;
            foreach (var e in message.Headers) mime.Headers.Add(e.Key, e.Value);

            var builder = new BodyBuilder();
            if (message.IsBodyHtml) builder.HtmlBody = message.Body;
            else builder.TextBody = message.Body;
            foreach (var e in message.Attachments)
            {
                builder.Attachments.Add(e.FileName, Convert.FromBase64String(e.ContentBase64));
            }
            mime.Body = builder.ToMessageBody();
            return mime;
        }
    }
}
