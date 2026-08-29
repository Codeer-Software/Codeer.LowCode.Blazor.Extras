using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using System.Net.Http.Json;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// SmtpMailSender の実送信確認。ローカルの smtp4dev (dotnet tool install -g Rnwood.Smtp4dev) に送り、
    /// その REST API で受信内容を検証する。smtp4dev が起動していなければ Ignore。
    /// 環境変数 SMTP4DEV_SMTP_PORT / SMTP4DEV_UI で既定 (25 / http://localhost:5000) を変更できる。
    /// </summary>
    [Explicit("requires local smtp4dev")]
    public class Smtp4devRealSendTest
    {
        static readonly string SmtpPort = Environment.GetEnvironmentVariable("SMTP4DEV_SMTP_PORT") ?? "25";
        static readonly string UiBase = Environment.GetEnvironmentVariable("SMTP4DEV_UI") ?? "http://localhost:5000";
        static readonly HttpClient Api = new() { BaseAddress = new Uri(UiBase), Timeout = TimeSpan.FromSeconds(10) };

        static SmtpSettings Settings(bool ssl) => new()
        {
            Host = "localhost",
            Port = SmtpPort,
            SSL = ssl.ToString(),
            SenderMailAddress = "noreply@example.com",
            SenderDisplayName = "CLB Smtp Test",
        };

        [SetUp]
        public async Task SetUp()
        {
            try
            {
                (await Api.DeleteAsync("/api/messages/*")).EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                Assert.Ignore($"smtp4dev is not running at {UiBase}: {e.Message}");
            }
        }

        [Test]
        public async Task 単発_HTML_添付_ReplyTo_ヘッダ()
        {
            var sender = new SmtpMailSender(Settings(ssl: false));
            var result = await sender.SendAsync(new MailMessage
            {
                To = { "a@example.com" },
                Cc = { "b@example.com" },
                Bcc = { "c@example.com" },
                Subject = "件名テスト 日本語",
                Body = "<p>本文 <b>太字</b></p>",
                IsBodyHtml = true,
                ReplyTo = "reply@example.com",
                Attachments = { new MailAttachment { FileName = "a.txt", ContentBase64 = Convert.ToBase64String("ABC"u8.ToArray()) } },
                Headers = { ["X-CLB-Original-To"] = "orig@example.com" },
            });
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(f => f.Error)));
            Assert.That(result.SuccessCount, Is.EqualTo(1));

            var messages = await GetMessagesAsync(1);
            var m = messages.Single();
            Assert.That(m.GetProperty("subject").GetString(), Is.EqualTo("件名テスト 日本語"));
            Assert.That(m.GetProperty("from").GetString(), Does.Contain("noreply@example.com"));
            var to = m.GetProperty("to").EnumerateArray().Select(e => e.GetString()).ToList();
            //to = envelope recipients (Bcc を含む)
            Assert.That(to, Is.EquivalentTo(new[] { "a@example.com", "b@example.com", "c@example.com" }));

            var source = await Api.GetStringAsync($"/api/messages/{m.GetProperty("id").GetString()}/raw");
            Assert.That(source, Does.Contain("Reply-To: reply@example.com"));
            Assert.That(source, Does.Contain("X-CLB-Original-To: orig@example.com"));
            Assert.That(source, Does.Contain("Content-Type: text/html"));
            Assert.That(source, Does.Contain("name=a.txt").Or.Contain("filename=a.txt"));
            Assert.That(source, Does.Not.Contain("Bcc: c@example.com"), "Bcc header must not be in the message");
        }

        [Test]
        public async Task 単発_プレーンテキスト()
        {
            var sender = new SmtpMailSender(Settings(ssl: false));
            var result = await sender.SendAsync(new MailMessage { To = { "a@example.com" }, Subject = "plain", Body = "line1\r\nline2" });
            Assert.That(result.IsSuccess, Is.True);

            var m = (await GetMessagesAsync(1)).Single();
            var text = await Api.GetStringAsync($"/api/messages/{m.GetProperty("id").GetString()}/plaintext");
            Assert.That(text.Trim(), Is.EqualTo("line1\r\nline2").Or.EqualTo("line1\nline2"));
        }

        [Test]
        public async Task バルク_宛先ごとに差し込まれる()
        {
            var sender = new SmtpMailSender(Settings(ssl: false));
            var template = new MailBulkTemplate
            {
                Subject = "{Name} 様へのお知らせ",
                Body = "{Name} 様\r\n残高は {Amount} 円です。",
            };
            var recipients = Enumerable.Range(1, 3).Select(i => new MailBulkRecipient
            {
                To = $"user{i}@example.com",
                Variables = { ["Name"] = $"ユーザー{i}", ["Amount"] = (i * 1000).ToString() },
            }).ToList();

            var result = await sender.SendBulkAsync(template, recipients);
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(f => f.Error)));
            Assert.That(result.SuccessCount, Is.EqualTo(3));

            var messages = await GetMessagesAsync(3);
            foreach (var i in Enumerable.Range(1, 3))
            {
                var m = messages.Single(e => e.GetProperty("to").EnumerateArray().Any(t => t.GetString() == $"user{i}@example.com"));
                Assert.That(m.GetProperty("subject").GetString(), Is.EqualTo($"ユーザー{i} 様へのお知らせ"));
                var text = await Api.GetStringAsync($"/api/messages/{m.GetProperty("id").GetString()}/plaintext");
                Assert.That(text, Does.Contain($"残高は {i * 1000} 円です。"));
            }
        }

        [Test]
        public async Task 接続失敗は例外ではなく失敗結果()
        {
            var settings = Settings(ssl: false);
            settings.Port = "1"; //何も聞いていないポート
            var sender = new SmtpMailSender(settings);
            var result = await sender.SendAsync(new MailMessage { To = { "a@example.com" }, Subject = "x", Body = "y" });
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures.Single().Error, Is.Not.Empty);
        }

        static async Task<List<JsonElement>> GetMessagesAsync(int expectedCount)
        {
            for (var i = 0; i < 50; i++)
            {
                var page = await Api.GetFromJsonAsync<JsonElement>("/api/messages?pageSize=100");
                var results = page.GetProperty("results").EnumerateArray().ToList();
                if (results.Count >= expectedCount) return results;
                await Task.Delay(100);
            }
            Assert.Fail($"smtp4dev did not receive {expectedCount} message(s)");
            return null!;
        }
    }
}
