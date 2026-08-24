using Codeer.LowCode.Blazor.Extras.Server.Mail;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailProviderSettingsTest
    {
        [Test]
        public void Normalize_旧MailSettingsはSmtp設定に写る()
        {
            var legacy = new MailSettings
            {
                Host = "smtp.example.com",
                Port = "587",
                SSL = "true",
                Password = "pass",
                SenderMailAddress = "noreply@example.com",
                SenderDisplayName = "System",
            };
            var config = new { Smtp = SmtpSettings.Normalize(null, legacy) };

            Assert.That(config.Smtp.Host, Is.EqualTo("smtp.example.com"));
            Assert.That(config.Smtp.Port, Is.EqualTo("587"));
            Assert.That(config.Smtp.SSL, Is.EqualTo("true"));
            Assert.That(config.Smtp.Password, Is.EqualTo("pass"));
            Assert.That(config.Smtp.SenderMailAddress, Is.EqualTo("noreply@example.com"));
            Assert.That(config.Smtp.SenderDisplayName, Is.EqualTo("System"));
        }

        [Test]
        public void Normalize_新設定にSMTPがあれば旧設定は無視される()
        {
            var result = SmtpSettings.Normalize(new SmtpSettings { Host = "new.example.com" },
                new MailSettings { Host = "old.example.com" });

            Assert.That(result.Host, Is.EqualTo("new.example.com"));
        }

        [Test]
        public void Normalize_両方nullでも空設定が返る()
        {
            Assert.That(SmtpSettings.Normalize(null, null).Host, Is.Empty);
            Assert.That(new GmailSettings().ClientSecret, Is.Empty);
        }
    }
}
