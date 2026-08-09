using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using System.Net;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class GraphApiMailSenderTest
    {
        static readonly MailSenderSettings Settings = new()
        {
            Name = "Notify",
            Type = MailSenderTypes.GraphApi,
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "secret",
            SenderMailAddress = "system@example.com",
        };

        static FakeHttpHandler CreateHandler()
        {
            var handler = new FakeHttpHandler();
            handler.Responder = (request, _) =>
                request.RequestUri!.Host == "login.microsoftonline.com"
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"access_token":"TOKEN1","expires_in":3600}""")
                    }
                    : new HttpResponseMessage(HttpStatusCode.Accepted);
            return handler;
        }

        [Test]
        public async Task 単発_トークン取得とsendMailのリクエスト形状()
        {
            var handler = CreateHandler();
            var sender = new GraphApiMailSender(Settings, handler.CreateClient());

            var result = await sender.SendAsync(new MailMessage
            {
                To = { "a@example.com" },
                Bcc = { "b@example.com" },
                Subject = "件名",
                Body = "本文",
                ReplyTo = "reply@example.com",
                Attachments = { new MailAttachment { FileName = "a.pdf", ContentBase64 = "QUJD" } },
                Headers = { ["X-CLB-Original-To"] = "orig@example.com", ["Bad-Header"] = "dropped" },
            });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));

            //① トークン取得 (client credentials)
            var (tokenRequest, tokenBody) = handler.Requests[0];
            Assert.That(tokenRequest.RequestUri!.ToString(), Does.Contain("login.microsoftonline.com/tenant-id/oauth2/v2.0/token"));
            Assert.That(tokenBody, Does.Contain("grant_type=client_credentials"));
            Assert.That(tokenBody, Does.Contain("client_id=client-id"));

            //② sendMail
            var (sendRequest, sendBody) = handler.Requests[1];
            Assert.That(sendRequest.RequestUri!.ToString(), Is.EqualTo("https://graph.microsoft.com/v1.0/users/system%40example.com/sendMail"));
            Assert.That(sendRequest.Headers.Authorization!.ToString(), Is.EqualTo("Bearer TOKEN1"));

            using var json = JsonDocument.Parse(sendBody);
            var message = json.RootElement.GetProperty("message");
            Assert.That(message.GetProperty("subject").GetString(), Is.EqualTo("件名"));
            Assert.That(message.GetProperty("body").GetProperty("contentType").GetString(), Is.EqualTo("Text"));
            Assert.That(message.GetProperty("body").GetProperty("content").GetString(), Is.EqualTo("本文"));
            Assert.That(message.GetProperty("toRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString(),
                Is.EqualTo("a@example.com"));
            Assert.That(message.GetProperty("bccRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString(),
                Is.EqualTo("b@example.com"));
            Assert.That(message.GetProperty("replyTo")[0].GetProperty("emailAddress").GetProperty("address").GetString(),
                Is.EqualTo("reply@example.com"));
            var attachment = message.GetProperty("attachments")[0];
            Assert.That(attachment.GetProperty("@odata.type").GetString(), Is.EqualTo("#microsoft.graph.fileAttachment"));
            Assert.That(attachment.GetProperty("contentBytes").GetString(), Is.EqualTo("QUJD"));
            //カスタムヘッダは x- で始まるものだけGraphに渡す
            var headers = message.GetProperty("internetMessageHeaders");
            Assert.That(headers.GetArrayLength(), Is.EqualTo(1));
            Assert.That(headers[0].GetProperty("name").GetString(), Is.EqualTo("X-CLB-Original-To"));
            Assert.That(json.RootElement.GetProperty("saveToSentItems").GetBoolean(), Is.True);
        }

        [Test]
        public async Task トークンはキャッシュされ2通目では再取得しない()
        {
            var handler = CreateHandler();
            var sender = new GraphApiMailSender(Settings, handler.CreateClient());

            await sender.SendAsync(new MailMessage { To = { "a@example.com" } });
            await sender.SendAsync(new MailMessage { To = { "b@example.com" } });

            Assert.That(handler.Requests.Count(e => e.Request.RequestUri!.Host == "login.microsoftonline.com"), Is.EqualTo(1));
            Assert.That(handler.Requests.Count(e => e.Request.RequestUri!.Host == "graph.microsoft.com"), Is.EqualTo(2));
        }

        [Test]
        public async Task エラー応答は失敗として報告される()
        {
            var handler = CreateHandler();
            var baseResponder = handler.Responder;
            handler.Responder = (request, body) =>
                request.RequestUri!.Host == "graph.microsoft.com"
                    ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"error":{"message":"denied"}}""") }
                    : baseResponder(request, body);
            var sender = new GraphApiMailSender(Settings, handler.CreateClient());

            var result = await sender.SendAsync(new MailMessage { To = { "a@example.com" } });
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures[0].To, Is.EqualTo("a@example.com"));
            Assert.That(result.Failures[0].Error, Does.Contain("403"));
        }

        [Test]
        public async Task バルク_逐次送信され部分失敗が宛先単位で報告される()
        {
            var handler = CreateHandler();
            var baseResponder = handler.Responder;
            var sendCount = 0;
            handler.Responder = (request, body) =>
            {
                if (request.RequestUri!.Host != "graph.microsoft.com") return baseResponder(request, body);
                //2通目だけ失敗させる
                return ++sendCount == 2
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad address") }
                    : new HttpResponseMessage(HttpStatusCode.Accepted);
            };
            var sender = new GraphApiMailSender(Settings, handler.CreateClient());

            var recipients = Enumerable.Range(0, 3).Select(i => new MailBulkRecipient
            {
                To = $"user{i}@example.com",
                Variables = { ["Name"] = $"user{i}" },
            }).ToList();
            var result = await sender.SendBulkAsync(new MailBulkTemplate { Subject = "{Name} 様", Body = "b" }, recipients);

            Assert.That(result.TotalCount, Is.EqualTo(3));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.Failures.Single().To, Is.EqualTo("user1@example.com"));

            //差し込みはクライアント側(共有エンジン)で解決されている
            var firstSend = handler.Requests.First(e => e.Request.RequestUri!.Host == "graph.microsoft.com").Body;
            using var json = JsonDocument.Parse(firstSend);
            Assert.That(json.RootElement.GetProperty("message").GetProperty("subject").GetString(), Is.EqualTo("user0 様"));
        }

        [Test]
        public async Task スロットリング429はRetryAfterを待って再試行する()
        {
            var handler = CreateHandler();
            var baseResponder = handler.Responder;
            var sendCount = 0;
            handler.Responder = (request, body) =>
            {
                if (request.RequestUri!.Host != "graph.microsoft.com") return baseResponder(request, body);
                if (++sendCount == 1)
                {
                    var res = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent("throttled") };
                    res.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                    return res;
                }
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            };
            var sender = new GraphApiMailSender(Settings, handler.CreateClient());

            var result = await sender.SendAsync(new MailMessage { To = { "a@example.com" } });
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(sendCount, Is.EqualTo(2));
        }
    }
}
