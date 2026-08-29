using LegacyMailMessage = Codeer.LowCode.Blazor.Extras.ScriptObjects.MailMessage;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 旧 (0.5.0) のメール送信サービス。アプリの MailController が受けた <see cref="LegacyMailMessage"/> を SMTP で送る。
    /// 中身は <see cref="SmtpMailSender"/> (MailKit) に委譲する。0.5.0 のテンプレートを変更なしで動かすために残している。
    /// 新規は <see cref="MailDispatcher"/> + <see cref="IMailSender"/> (MailField / BulkMailField) を使う。
    /// </summary>
    public class SmtpMailService
    {
        readonly MailSettings _settings;

        public SmtpMailService(MailSettings settings) => _settings = settings;

        /// <summary>送れたら true。設定不足 (Host / Port) や宛先なし、送信失敗は false (旧仕様どおり例外にしない)。</summary>
        public async Task<bool> SendAsync(LegacyMailMessage message)
        {
            if (string.IsNullOrEmpty(_settings.Host)) return false;
            if (!int.TryParse(_settings.Port, out _)) return false;
            if (!message.To.Any()) return false;

            var result = await new SmtpMailSender(_settings).SendAsync(message.ToMailMessage());
            return result.IsSuccess;
        }
    }
}
