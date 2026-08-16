using Codeer.LowCode.Blazor.Extras.Mail;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Legacy single-SMTP entry point kept for backward compatibility.
    /// Internally delegates to <see cref="SmtpMailSender"/> (MailKit).
    /// New code should use <see cref="MailDispatcher"/> with named senders.
    /// </summary>
    public class SmtpMailService
    {
        readonly MailSettings _settings;

        public SmtpMailService(MailSettings settings) => _settings = settings;

        public async Task<bool> SendAsync(MailMessage message)
        {
            if (string.IsNullOrEmpty(_settings.Host)) return false;
            if (!message.To.Any()) return false;

            var sender = new SmtpMailSender(new MailInfraSettings
            {
                Name = MailInfraSettings.LegacyDefaultName,
                Type = MailInfraTypes.Smtp,
                Host = _settings.Host,
                Port = _settings.Port,
                SSL = _settings.SSL,
                Password = _settings.Password,
                SenderMailAddress = _settings.SenderMailAddress,
                SenderDisplayName = _settings.SenderDisplayName,
            });
            return (await sender.SendAsync(message)).IsSuccess;
        }
    }
}
