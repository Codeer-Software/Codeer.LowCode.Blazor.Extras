using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Provider-independent send layer: resolves named senders, applies the redirect-all
    /// safety net and the bulk count limit, then dispatches to <see cref="IMailSender"/>.
    /// Custom infrastructures can be plugged in via the customSenderFactory constructor argument.
    /// </summary>
    public class MailDispatcher
    {
        /// <summary>Header recording the original recipients when RedirectAllTo is active.</summary>
        public const string OriginalToHeader = "X-CLB-Original-To";

        /// <summary>Header recording the original recipient count of a redirected bulk send.</summary>
        public const string OriginalTotalHeader = "X-CLB-Original-Total";

        //redirected bulk sends are clipped so that a staging environment never sends thousands of mails
        internal const int RedirectBulkClipCount = 10;

        readonly MailConfig _config;
        readonly Func<MailSenderSettings, IMailSender?>? _customSenderFactory;
        readonly MailHistoryWriter? _historyWriter;

        public MailDispatcher(MailConfig config, Func<MailSenderSettings, IMailSender?>? customSenderFactory = null,
            MailHistoryWriter? historyWriter = null)
        {
            _config = config;
            _customSenderFactory = customSenderFactory;
            _historyWriter = historyWriter;
        }

        /// <summary>Resolves sender settings by name. Empty name means the first configured sender.</summary>
        public MailSenderSettings ResolveSenderSettings(string? senderName)
        {
            if (!_config.Senders.Any()) throw new InvalidOperationException("No mail senders are configured (Mail.Senders).");
            if (string.IsNullOrEmpty(senderName)) return _config.Senders[0];
            return _config.Senders.FirstOrDefault(e => e.Name == senderName)
                ?? throw new InvalidOperationException($"Mail sender '{senderName}' is not configured.");
        }

        public IMailSender CreateSender(MailSenderSettings settings)
        {
            var custom = _customSenderFactory?.Invoke(settings);
            if (custom != null) return custom;
            return settings.Type switch
            {
                MailSenderTypes.GraphApi => new GraphApiMailSender(settings),
                MailSenderTypes.SendGrid => new SendGridMailSender(settings),
                //empty type = legacy configs are SMTP
                MailSenderTypes.Smtp or "" => new SmtpMailSender(settings),
                _ => throw new InvalidOperationException($"Unknown mail sender type '{settings.Type}'."),
            };
        }

        /// <summary>Sends the single-send wire request (POST /api/mail) as-is. Controllers stay thin.</summary>
        public async Task<MailSendResult> SendAsync(MailSendRequest request)
            => await SendAsync(request.SenderName, request.Message, CreateSource(request.SourceModule, request.SourceId));

        internal static MailHistorySource? CreateSource(string sourceModule, string sourceId)
            => string.IsNullOrEmpty(sourceModule)
                ? null
                : new MailHistorySource { SourceModule = sourceModule, SourceId = sourceId };

        public async Task<MailSendResult> SendAsync(string? senderName, MailMessage message, MailHistorySource? source = null)
        {
            if (!message.To.Any() && !message.Cc.Any() && !message.Bcc.Any())
                return MailSendResult.Failure(string.Empty, "No recipients.");

            var settings = ResolveSenderSettings(senderName);
            var sender = CreateSender(settings);
            var sendMessage = string.IsNullOrEmpty(_config.RedirectAllTo) ? message : Redirect(message);
            var result = await sender.SendAsync(sendMessage);
            if (_historyWriter != null) await _historyWriter.WriteAsync(settings.Name, message.Subject, result, source);
            return result;
        }

        /// <summary>
        /// Sends one template to many recipients. Exceeding the sender's MaxBulkCount throws
        /// (never silently truncates). For HTML templates the variable values are HTML-encoded
        /// once here so native bulk (SendGrid) and sequential fallbacks behave the same.
        /// </summary>
        public async Task<MailSendResult> SendBulkAsync(string? senderName, MailBulkTemplate template, List<MailBulkRecipient> recipients, MailHistorySource? source = null)
        {
            var settings = ResolveSenderSettings(senderName);
            if (recipients.Count > settings.MaxBulkCount)
                throw new InvalidOperationException(
                    $"Bulk send of {recipients.Count} mails exceeds MaxBulkCount ({settings.MaxBulkCount}) of mail sender '{settings.Name}'.");

            if (template.IsBodyHtml) recipients = recipients.Select(EncodeHtmlVariables).ToList();

            var sender = CreateSender(settings);
            var result = !string.IsNullOrEmpty(_config.RedirectAllTo)
                ? await SendBulkRedirectedAsync(sender, template, recipients)
                : await sender.SendBulkAsync(template, recipients);
            if (_historyWriter != null) await _historyWriter.WriteAsync(settings.Name, template.Subject, result, source);
            return result;
        }

        //injection guard: variable values may contain user input
        static MailBulkRecipient EncodeHtmlVariables(MailBulkRecipient recipient)
            => new()
            {
                To = recipient.To,
                Cc = recipient.Cc,
                Bcc = recipient.Bcc,
                Variables = recipient.Variables.ToDictionary(e => e.Key, e => WebUtility.HtmlEncode(e.Value)),
            };

        MailMessage Redirect(MailMessage src)
        {
            var originalTo = $"to: {string.Join(";", src.To)} cc: {string.Join(";", src.Cc)} bcc: {string.Join(";", src.Bcc)}";
            var redirected = new MailMessage
            {
                To = { _config.RedirectAllTo },
                Subject = src.Subject,
                Body = src.Body,
                IsBodyHtml = src.IsBodyHtml,
                ReplyTo = src.ReplyTo,
                Attachments = src.Attachments,
                Headers = new Dictionary<string, string>(src.Headers),
            };
            redirected.Headers[OriginalToHeader] = originalTo;
            return redirected;
        }

        async Task<MailSendResult> SendBulkRedirectedAsync(IMailSender sender, MailBulkTemplate template, List<MailBulkRecipient> recipients)
        {
            var result = new MailSendResult { TotalCount = recipients.Count, SuccessCount = recipients.Count };
            foreach (var recipient in recipients.Take(RedirectBulkClipCount))
            {
                var message = SmtpMailSender.CreateResolvedMessage(template, recipient);
                message = Redirect(message);
                message.Headers[OriginalTotalHeader] = recipients.Count.ToString();
                var sendResult = await sender.SendAsync(message);
                if (!sendResult.IsSuccess)
                {
                    result.SuccessCount--;
                    result.Failures.Add(new MailSendFailure { To = recipient.To, Error = sendResult.Failures[0].Error });
                }
            }
            return result;
        }
    }
}
