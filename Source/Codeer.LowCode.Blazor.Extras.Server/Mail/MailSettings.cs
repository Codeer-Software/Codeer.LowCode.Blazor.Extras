namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 旧 (0.5.0) のメール設定 (appsettings の "MailSettings" セクション)。項目は <see cref="SmtpSettings"/> と同じ
    /// (Host / Port / SSL / SenderMailAddress / SenderDisplayName / Password)。0.5.0 のテンプレート (Program.cs / SystemConfig) を
    /// 変更なしで動かすために残している。新規は <see cref="SmtpSettings"/> を使う。
    /// </summary>
    public class MailSettings : SmtpSettings
    {
    }
}
