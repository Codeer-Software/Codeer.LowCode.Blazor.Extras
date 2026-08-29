using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Mail;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>0.5.0 のメール API (MailService / MailMessage / SmtpMailService / MailSettings) が互換のまま残っていることの固定。</summary>
    public class LegacyMailApiTest
    {
        [Test]
        public void MailMessageの組み立てと新APIへの変換()
        {
            var message = new MailService().CreateMessage()
                .AddTo("a@example.com; b@example.com")
                .AddCc("c@example.com")
                .AddBcc("d@example.com")
                .SetSubject("件名")
                .SetHtmlBody("<b>本文</b>")
                .SetReplyTo("reply@example.com")
                .AddTextAttachment("a.txt", "ABC");

            Assert.That(message.To, Is.EqualTo(new[] { "a@example.com", "b@example.com" }));
            Assert.That(message.IsBodyHtml, Is.True);

            var converted = message.ToMailMessage();
            Assert.That(converted.To, Is.EqualTo(message.To));
            Assert.That(converted.Cc, Is.EqualTo(new[] { "c@example.com" }));
            Assert.That(converted.Bcc, Is.EqualTo(new[] { "d@example.com" }));
            Assert.That(converted.Subject, Is.EqualTo("件名"));
            Assert.That(converted.Body, Is.EqualTo("<b>本文</b>"));
            Assert.That(converted.IsBodyHtml, Is.True);
            Assert.That(converted.ReplyTo, Is.EqualTo("reply@example.com"));
            Assert.That(converted.Attachments.Single().FileName, Is.EqualTo("a.txt"));
            Assert.That(Convert.FromBase64String(converted.Attachments.Single().ContentBase64), Is.EqualTo("ABC"u8.ToArray()));
        }

        [Test]
        public void SetBodyはプレーンテキストに戻す()
        {
            var message = new MailMessage().SetHtmlBody("<b>x</b>").SetBody("plain");
            Assert.That(message.IsBodyHtml, Is.False);
            Assert.That(message.Body, Is.EqualTo("plain"));
        }

        [Test]
        public async Task SmtpMailServiceは設定不足や宛先なしをfalseで返す()
        {
            var message = new MailMessage().AddTo("a@example.com").SetSubject("s").SetBody("b");
            Assert.That(await new SmtpMailService(new MailSettings()).SendAsync(message), Is.False, "Host なし");
            Assert.That(await new SmtpMailService(new MailSettings { Host = "localhost", Port = "abc" }).SendAsync(message), Is.False, "Port が数値でない");
            Assert.That(await new SmtpMailService(new MailSettings { Host = "localhost", Port = "25" }).SendAsync(new MailMessage()), Is.False, "宛先なし");
        }

        [Test]
        public void MailSettingsはSmtpSettingsとして使える()
        {
            var settings = new MailSettings { Host = "smtp.example.com", Port = "587", SSL = "true", SenderMailAddress = "s@example.com", Password = "p" };
            SmtpSettings smtp = settings;
            Assert.That(smtp.Host, Is.EqualTo("smtp.example.com"));
            Assert.That(smtp.UserName, Is.Empty, "旧設定に無い項目は既定値 (空 = SenderMailAddress で認証)");
        }

        [Test]
        public async Task MailServiceはフックが設定されていればそれで送る()
        {
            var original = MailService.SendMailAsyncCore;
            try
            {
                MailMessage? sent = null;
                MailService.SendMailAsyncCore = m => { sent = m; return Task.FromResult(true); };
                var ok = await new MailService().SendEmailAsync("a@example.com", "s", "b");
                Assert.That(ok, Is.True);
                Assert.That(sent!.To, Is.EqualTo(new[] { "a@example.com" }));
                Assert.That(sent.Subject, Is.EqualTo("s"));
            }
            finally
            {
                MailService.SendMailAsyncCore = original;
            }
        }
    }
}
