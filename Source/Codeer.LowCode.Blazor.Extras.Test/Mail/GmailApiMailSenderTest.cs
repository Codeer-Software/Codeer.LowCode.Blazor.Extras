using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class GmailApiMailSenderTest
    {
        //テスト用に生成したRSA鍵でサービスアカウントJSONキーを偽装する(ClientSecret=JSON文字列の経路も兼ねる)
        static readonly MailSenderSettings Settings = new()
        {
            Name = "Gmail",
            Type = MailSenderTypes.GmailApi,
            SenderMailAddress = "notify@example.com",
            SenderDisplayName = "業務システム",
            ClientSecret = JsonSerializer.Serialize(new
            {
                client_email = "svc@project.iam.gserviceaccount.com",
                private_key = RSA.Create(2048).ExportPkcs8PrivateKeyPem(),
            }),
        };

        static FakeHttpHandler CreateHandler()
        {
            var handler = new FakeHttpHandler();
            handler.Responder = (request, _) =>
                request.RequestUri!.Host == "oauth2.googleapis.com"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"TOKEN1","expires_in":3600}""")
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK);
            return handler;
        }

        static string DecodeBase64Url(string value)
        {
            value = value.Replace('-', '+').Replace('_', '/');
            return Encoding.UTF8.GetString(Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '=')));
        }

        [Test]
        public async Task 単発_JWT取得とsendのリクエスト形状()
        {
            var handler = CreateHandler();
            var sender = new GmailApiMailSender(Settings, handler.CreateClient());

            var result = await sender.SendAsync(new MailMessage
            {
                To = { "a@example.com" },
                Subject = "Order Confirmed",
                Body = "本文",
            });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));

            //① トークン取得 (jwt-bearer。assertion のクレームに サービスアカウント/委任ユーザー/スコープ)
            var (tokenRequest, tokenBody) = handler.Requests[0];
            Assert.That(tokenRequest.RequestUri!.ToString(), Is.EqualTo("https://oauth2.googleapis.com/token"));
            Assert.That(tokenBody, Does.Contain("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer"));
            var assertion = tokenBody.Split('&').Single(e => e.StartsWith("assertion=")).Substring("assertion=".Length);
            var claims = JsonDocument.Parse(DecodeBase64Url(Uri.UnescapeDataString(assertion).Split('.')[1])).RootElement;
            Assert.That(claims.GetProperty("iss").GetString(), Is.EqualTo("svc@project.iam.gserviceaccount.com"));
            Assert.That(claims.GetProperty("sub").GetString(), Is.EqualTo("notify@example.com"));
            Assert.That(claims.GetProperty("scope").GetString(), Is.EqualTo("https://www.googleapis.com/auth/gmail.send"));

            //② send (raw = base64url の MIME 全文)
            var (sendRequest, sendBody) = handler.Requests[1];
            Assert.That(sendRequest.RequestUri!.ToString(), Is.EqualTo("https://gmail.googleapis.com/gmail/v1/users/me/messages/send"));
            Assert.That(sendRequest.Headers.Authorization!.ToString(), Is.EqualTo("Bearer TOKEN1"));
            var raw = JsonDocument.Parse(sendBody).RootElement.GetProperty("raw").GetString()!;
            var mime = DecodeBase64Url(raw);
            Assert.That(mime, Does.Contain("Subject: Order Confirmed"));
            Assert.That(mime, Does.Contain("To: a@example.com"));
            Assert.That(mime, Does.Contain("notify@example.com")); //From = 設定の送信ユーザー
        }

        [Test]
        public async Task トークンはキャッシュされ2通目では再取得しない()
        {
            var handler = CreateHandler();
            var sender = new GmailApiMailSender(Settings, handler.CreateClient());

            await sender.SendAsync(new MailMessage { To = { "a@example.com" } });
            await sender.SendAsync(new MailMessage { To = { "b@example.com" } });

            Assert.That(handler.Requests.Count(e => e.Request.RequestUri!.Host == "oauth2.googleapis.com"), Is.EqualTo(1));
            Assert.That(handler.Requests.Count(e => e.Request.RequestUri!.Host == "gmail.googleapis.com"), Is.EqualTo(2));
        }

        [Test]
        public async Task エラー応答は失敗として報告される()
        {
            var handler = CreateHandler();
            var baseResponder = handler.Responder;
            handler.Responder = (request, body) =>
                request.RequestUri!.Host == "gmail.googleapis.com"
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"error":{"message":"denied"}}""") }
                    : baseResponder(request, body);
            var sender = new GmailApiMailSender(Settings, handler.CreateClient());

            var result = await sender.SendAsync(new MailMessage { To = { "a@example.com" } });
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures[0].To, Is.EqualTo("a@example.com"));
            Assert.That(result.Failures[0].Error, Does.Contain("403"));
        }

        [Test]
        public async Task バルク_逐次送信され差し込みが解決される()
        {
            var handler = CreateHandler();
            var sender = new GmailApiMailSender(Settings, handler.CreateClient());

            var recipients = Enumerable.Range(0, 2).Select(i => new MailBulkRecipient
            {
                To = $"user{i}@example.com",
                Variables = { ["Name"] = $"user{i}" },
            }).ToList();
            var result = await sender.SendBulkAsync(new MailBulkTemplate { Subject = "Hello {Name}", Body = "b" }, recipients);

            Assert.That(result.SuccessCount, Is.EqualTo(2));
            var firstSend = handler.Requests.First(e => e.Request.RequestUri!.Host == "gmail.googleapis.com").Body;
            var raw = JsonDocument.Parse(firstSend).RootElement.GetProperty("raw").GetString()!;
            Assert.That(DecodeBase64Url(raw), Does.Contain("Subject: Hello user0"));
        }
    }
}
