using Codeer.LowCode.Blazor.Extras.Mail;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 後方互換のために残している旧形式の単一 SMTP 送信の入口。
    /// 内部では <see cref="SmtpMailSender"/> (MailKit) へ委譲する。
    /// 新しいコードは名前付きインフラの <see cref="MailDispatcher"/> を使うこと。
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
