using Codeer.LowCode.Blazor.Extras.ScriptObjects;
//ScriptObjects には旧 API (0.5.0 互換) の MailMessage / MailAttachment もあるので、こちらは新 API の型を使う
using MailAttachment = Codeer.LowCode.Blazor.Extras.Mail.MailAttachment;
using MailMessage = Codeer.LowCode.Blazor.Extras.Mail.MailMessage;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using System.Net;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class SendGridMailSenderTest
    {
        static readonly SendGridSettings Settings = new()
        {
            ApiKey = "SG.test",
            SenderMailAddress = "info@example.com",
            SenderDisplayName = "Info",
        };

        [Test]
        public async Task 単発_リクエスト形状()
        {
            var handler = new FakeHttpHandler();
            var sender = new SendGridMailSender(Settings, handler.CreateClient());

            var result = await sender.SendAsync(new MailMessage
            {
                To = { "a@example.com" },
                Cc = { "b@example.com" },
                Subject = "件名",
                Body = "<p>本文</p>",
                IsBodyHtml = true,
                ReplyTo = "reply@example.com",
                Attachments = { new MailAttachment { FileName = "a.pdf", ContentBase64 = "QUJD" } },
                Headers = { ["X-CLB-Original-To"] = "orig@example.com" },
            });

            Assert.That(result.IsSuccess, Is.True);
            var (request, body) = handler.Requests.Single();
            Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://api.sendgrid.com/v3/mail/send"));
            Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer SG.test"));

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            Assert.That(root.GetProperty("from").GetProperty("email").GetString(), Is.EqualTo("info@example.com"));
            Assert.That(root.GetProperty("from").GetProperty("name").GetString(), Is.EqualTo("Info"));
            Assert.That(root.GetProperty("subject").GetString(), Is.EqualTo("件名"));
            var content = root.GetProperty("content")[0];
            Assert.That(content.GetProperty("type").GetString(), Is.EqualTo("text/html"));
            Assert.That(content.GetProperty("value").GetString(), Is.EqualTo("<p>本文</p>"));
            var personalization = root.GetProperty("personalizations")[0];
            Assert.That(personalization.GetProperty("to")[0].GetProperty("email").GetString(), Is.EqualTo("a@example.com"));
            Assert.That(personalization.GetProperty("cc")[0].GetProperty("email").GetString(), Is.EqualTo("b@example.com"));
            Assert.That(root.GetProperty("reply_to").GetProperty("email").GetString(), Is.EqualTo("reply@example.com"));
            Assert.That(root.GetProperty("attachments")[0].GetProperty("filename").GetString(), Is.EqualTo("a.pdf"));
            Assert.That(root.GetProperty("headers").GetProperty("X-CLB-Original-To").GetString(), Is.EqualTo("orig@example.com"));
        }

        [Test]
        public async Task バルク_1001件は2リクエストに分割され差し込みはsubstitutionsに乗る()
        {
            var handler = new FakeHttpHandler();
            var sender = new SendGridMailSender(Settings, handler.CreateClient());

            var recipients = Enumerable.Range(0, 1001).Select(i => new MailBulkRecipient
            {
                To = $"user{i}@example.com",
                Variables = { ["Name"] = $"user{i}" },
            }).ToList();

            var result = await sender.SendBulkAsync(
                new MailBulkTemplate { Subject = "{Name} 様", Body = "こんにちは {Name} さん" }, recipients);

            Assert.That(result.SuccessCount, Is.EqualTo(1001));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));

            using var first = JsonDocument.Parse(handler.Requests[0].Body);
            var personalizations = first.RootElement.GetProperty("personalizations");
            Assert.That(personalizations.GetArrayLength(), Is.EqualTo(1000));
            var p0 = personalizations[0];
            Assert.That(p0.GetProperty("to")[0].GetProperty("email").GetString(), Is.EqualTo("user0@example.com"));
            Assert.That(p0.GetProperty("substitutions").GetProperty("{Name}").GetString(), Is.EqualTo("user0"));
            //本文はテンプレのまま(置換はSendGrid側)
            Assert.That(first.RootElement.GetProperty("content")[0].GetProperty("value").GetString(), Is.EqualTo("こんにちは {Name} さん"));

            using var second = JsonDocument.Parse(handler.Requests[1].Body);
            Assert.That(second.RootElement.GetProperty("personalizations").GetArrayLength(), Is.EqualTo(1));
        }

        [Test]
        public async Task バルク_チャンク失敗はそのチャンクの宛先が失敗として報告される()
        {
            var handler = new FakeHttpHandler();
            var count = 0;
            handler.Responder = (_, _) => ++count == 1
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"errors":[{"message":"bad"}]}""") }
                : new HttpResponseMessage(HttpStatusCode.Accepted);
            var sender = new SendGridMailSender(Settings, handler.CreateClient());

            var recipients = Enumerable.Range(0, 1001).Select(i => new MailBulkRecipient { To = $"user{i}@example.com" }).ToList();
            var result = await sender.SendBulkAsync(new MailBulkTemplate { Subject = "s", Body = "b" }, recipients);

            Assert.That(result.TotalCount, Is.EqualTo(1001));
            Assert.That(result.Failures, Has.Count.EqualTo(1000)); //先頭チャンクが全滅
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.Failures[0].Error, Does.Contain("400"));
        }

        [Test]
        public async Task 空本文はスペースに置き換える()
        {
            //SendGridは空contentを拒否するため
            var handler = new FakeHttpHandler();
            var sender = new SendGridMailSender(Settings, handler.CreateClient());
            await sender.SendAsync(new MailMessage { To = { "a@example.com" }, Subject = "s" });

            using var json = JsonDocument.Parse(handler.Requests.Single().Body);
            Assert.That(json.RootElement.GetProperty("content")[0].GetProperty("value").GetString(), Is.EqualTo(" "));
        }
    }
}
