using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using MimeKit;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// SMTP実送信はサーバーが要るため、MimeMessageの構築とテンプレ解決の対応だけを固定する。
    /// </summary>
    public class SmtpMailSenderTest
    {
        static readonly MailInfraSettings Settings = new()
        {
            Name = "Local",
            Type = MailInfraTypes.Smtp,
            Host = "smtp.example.com",
            Port = "587",
            SenderMailAddress = "noreply@example.com",
            SenderDisplayName = "System",
        };

        [Test]
        public void CreateMimeMessage_基本要素の対応()
        {
            var mime = SmtpMailSender.CreateMimeMessage(Settings, new MailMessage
            {
                To = { "a@example.com", "b@example.com" },
                Cc = { "c@example.com" },
                Bcc = { "d@example.com" },
                Subject = "件名",
                Body = "本文",
                ReplyTo = "reply@example.com",
                Headers = { ["X-CLB-Original-To"] = "orig@example.com" },
            });

            var from = (MailboxAddress)mime.From.Single();
            Assert.That(from.Address, Is.EqualTo("noreply@example.com"));
            Assert.That(from.Name, Is.EqualTo("System"));
            Assert.That(mime.To.Mailboxes.Select(e => e.Address), Is.EqualTo(new[] { "a@example.com", "b@example.com" }));
            Assert.That(mime.Cc.Mailboxes.Single().Address, Is.EqualTo("c@example.com"));
            Assert.That(mime.Bcc.Mailboxes.Single().Address, Is.EqualTo("d@example.com"));
            Assert.That(mime.ReplyTo.Mailboxes.Single().Address, Is.EqualTo("reply@example.com"));
            Assert.That(mime.Subject, Is.EqualTo("件名"));
            Assert.That(mime.TextBody, Is.EqualTo("本文"));
            Assert.That(mime.Headers["X-CLB-Original-To"], Is.EqualTo("orig@example.com"));
        }

        [Test]
        public void CreateMimeMessage_動的Fromの上書き()
        {
            var mime = SmtpMailSender.CreateMimeMessage(Settings, new MailMessage
            {
                From = "sales@example.com",
                FromDisplayName = "営業 太郎",
                To = { "a@example.com" },
                Subject = "件名",
                Body = "本文",
            });

            var from = (MailboxAddress)mime.From.Single();
            Assert.That(from.Address, Is.EqualTo("sales@example.com"));
            Assert.That(from.Name, Is.EqualTo("営業 太郎"));
        }

        [Test]
        public void CreateResolvedMessage_テンプレートのFromが引き継がれる()
        {
            var message = SmtpMailSender.CreateResolvedMessage(
                new MailBulkTemplate { From = "sales@example.com", FromDisplayName = "営業 太郎", Subject = "s", Body = "b" },
                new MailBulkRecipient { To = "a@example.com" });
            Assert.That(message.From, Is.EqualTo("sales@example.com"));
            Assert.That(message.FromDisplayName, Is.EqualTo("営業 太郎"));
        }

        [Test]
        public void CreateMimeMessage_HTML本文と添付()
        {
            var mime = SmtpMailSender.CreateMimeMessage(Settings, new MailMessage
            {
                To = { "a@example.com" },
                Body = "<p>本文</p>",
                IsBodyHtml = true,
                Attachments = { new MailAttachment { FileName = "a.txt", ContentBase64 = Convert.ToBase64String("ABC"u8.ToArray()) } },
            });

            Assert.That(mime.HtmlBody, Is.EqualTo("<p>本文</p>"));
            var attachment = mime.Attachments.OfType<MimePart>().Single();
            Assert.That(attachment.FileName, Is.EqualTo("a.txt"));
        }

        [Test]
        public void CreateResolvedMessage_テンプレが宛先ごとの変数で解決される()
        {
            var message = SmtpMailSender.CreateResolvedMessage(
                new MailBulkTemplate
                {
                    Subject = "{Name} 様",
                    Body = "こんにちは {Name} さん",
                    IsBodyHtml = true,
                    ReplyTo = "reply@example.com",
                    Attachments = { new MailAttachment { FileName = "a.pdf", ContentBase64 = "QUJD" } },
                },
                new MailBulkRecipient
                {
                    To = "a@example.com",
                    Cc = { "c@example.com" },
                    Variables = { ["Name"] = "田中" },
                });

            Assert.That(message.To.Single(), Is.EqualTo("a@example.com"));
            Assert.That(message.Cc.Single(), Is.EqualTo("c@example.com"));
            Assert.That(message.Subject, Is.EqualTo("田中 様"));
            Assert.That(message.Body, Is.EqualTo("こんにちは 田中 さん"));
            Assert.That(message.IsBodyHtml, Is.True);
            Assert.That(message.ReplyTo, Is.EqualTo("reply@example.com"));
            Assert.That(message.Attachments.Single().FileName, Is.EqualTo("a.pdf"));
        }
    }
}
