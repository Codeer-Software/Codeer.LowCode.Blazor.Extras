using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailConfigTest
    {
        [Test]
        public void Normalize_旧MailSettingsはDefaultという名前のSmtpセンダーになる()
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
            var config = MailConfig.Normalize(null, legacy);

            Assert.That(config.Senders, Has.Count.EqualTo(1));
            var sender = config.Senders[0];
            Assert.That(sender.Name, Is.EqualTo("Default"));
            Assert.That(sender.Type, Is.EqualTo(MailSenderTypes.Smtp));
            Assert.That(sender.Host, Is.EqualTo("smtp.example.com"));
            Assert.That(sender.SenderMailAddress, Is.EqualTo("noreply@example.com"));
        }

        [Test]
        public void Normalize_新設定があれば旧設定は後ろに足される()
        {
            var config = new MailConfig
            {
                Senders = { new MailSenderSettings { Name = "Notify", Type = MailSenderTypes.GraphApi } }
            };
            var result = MailConfig.Normalize(config, new MailSettings { Host = "smtp.example.com" });

            Assert.That(result.Senders.Select(e => e.Name), Is.EqualTo(new[] { "Notify", "Default" }));
        }

        [Test]
        public void Normalize_同名Defaultがあれば旧設定は足さない()
        {
            var config = new MailConfig
            {
                Senders = { new MailSenderSettings { Name = "Default", Type = MailSenderTypes.SendGrid } }
            };
            var result = MailConfig.Normalize(config, new MailSettings { Host = "smtp.example.com" });

            Assert.That(result.Senders, Has.Count.EqualTo(1));
            Assert.That(result.Senders[0].Type, Is.EqualTo(MailSenderTypes.SendGrid));
        }

        [Test]
        public void Normalize_両方nullでも空設定が返る()
        {
            var result = MailConfig.Normalize(null, null);
            Assert.That(result.Senders, Is.Empty);
        }
    }
}
