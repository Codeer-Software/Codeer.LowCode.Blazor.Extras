using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailDispatcherTest
    {
        /// <summary>送られたメッセージ/バルクを記録するフェイクインフラ。</summary>
        class FakeMailSender : IMailSender
        {
            public List<MailMessage> Sent { get; } = new();
            public List<(MailBulkTemplate Template, List<MailBulkRecipient> Recipients)> BulkSent { get; } = new();

            public Task<MailSendResult> SendAsync(MailMessage message)
            {
                Sent.Add(message);
                return Task.FromResult(MailSendResult.Success(1));
            }

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
            {
                BulkSent.Add((template, recipients));
                return Task.FromResult(MailSendResult.Success(recipients.Count));
            }
        }

        static (MailDispatcher dispatcher, FakeMailSender fake) Create(string redirectAllTo = "", int maxBulkCount = 10000)
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                RedirectAllTo = redirectAllTo,
                Senders =
                {
                    new MailSenderSettings { Name = "Main", Type = MailSenderTypes.Smtp, MaxBulkCount = maxBulkCount },
                    new MailSenderSettings { Name = "Sub", Type = MailSenderTypes.SendGrid },
                }
            };
            return (new MailDispatcher(config, _ => fake), fake);
        }

        [Test]
        public void ResolveSenderSettings_省略は先頭_名前指定は一致_不明はエラー()
        {
            var (dispatcher, _) = Create();
            Assert.That(dispatcher.ResolveSenderSettings(null).Name, Is.EqualTo("Main"));
            Assert.That(dispatcher.ResolveSenderSettings("Sub").Name, Is.EqualTo("Sub"));
            Assert.That(() => dispatcher.ResolveSenderSettings("Nothing"), Throws.InvalidOperationException);
        }

        [Test]
        public void ResolveSenderSettings_設定なしはエラー()
        {
            var dispatcher = new MailDispatcher(new MailConfig());
            Assert.That(() => dispatcher.ResolveSenderSettings(null), Throws.InvalidOperationException);
        }

        [Test]
        public async Task Send_宛先なしは失敗を返す()
        {
            var (dispatcher, fake) = Create();
            var result = await dispatcher.SendAsync(null, new MailMessage());
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(fake.Sent, Is.Empty);
        }

        [Test]
        public async Task Send_Redirect時は宛先が差し替わり元宛先はヘッダに残る()
        {
            var (dispatcher, fake) = Create(redirectAllTo: "catch@example.com");
            await dispatcher.SendAsync(null, new MailMessage
            {
                To = { "a@example.com" },
                Cc = { "b@example.com" },
                Bcc = { "c@example.com" },
                Subject = "s",
            });

            var sent = fake.Sent.Single();
            Assert.That(sent.To, Is.EqualTo(new[] { "catch@example.com" }));
            Assert.That(sent.Cc, Is.Empty);
            Assert.That(sent.Bcc, Is.Empty);
            Assert.That(sent.Headers[MailDispatcher.OriginalToHeader],
                Is.EqualTo("to: a@example.com cc: b@example.com bcc: c@example.com"));
        }

        [Test]
        public async Task SendBulk_通常はインフラのバルクにそのまま渡る()
        {
            var (dispatcher, fake) = Create();
            var recipients = Enumerable.Range(0, 5).Select(i => new MailBulkRecipient { To = $"user{i}@example.com" }).ToList();
            var result = await dispatcher.SendBulkAsync("Main", new MailBulkTemplate { Subject = "s", Body = "b" }, recipients);

            Assert.That(result.SuccessCount, Is.EqualTo(5));
            Assert.That(fake.BulkSent.Single().Recipients, Has.Count.EqualTo(5));
        }

        [Test]
        public void SendBulk_MaxBulkCount超過はエラー()
        {
            var (dispatcher, _) = Create(maxBulkCount: 3);
            var recipients = Enumerable.Range(0, 4).Select(i => new MailBulkRecipient { To = $"user{i}@example.com" }).ToList();
            Assert.That(async () => await dispatcher.SendBulkAsync("Main", new MailBulkTemplate(), recipients),
                Throws.InvalidOperationException.With.Message.Contains("MaxBulkCount"));
        }

        [Test]
        public async Task SendBulk_HTML本文は差し込み値がエスケープされる()
        {
            var (dispatcher, fake) = Create();
            var recipients = new List<MailBulkRecipient>
            {
                new() { To = "a@example.com", Variables = { ["Name"] = "<b>&x</b>" } }
            };
            await dispatcher.SendBulkAsync(null, new MailBulkTemplate { Body = "{Name}", IsBodyHtml = true }, recipients);

            Assert.That(fake.BulkSent.Single().Recipients[0].Variables["Name"], Is.EqualTo("&lt;b&gt;&amp;x&lt;/b&gt;"));
        }

        [Test]
        public async Task SendBulk_テキスト本文は差し込み値をエスケープしない()
        {
            var (dispatcher, fake) = Create();
            var recipients = new List<MailBulkRecipient>
            {
                new() { To = "a@example.com", Variables = { ["Name"] = "<b>" } }
            };
            await dispatcher.SendBulkAsync(null, new MailBulkTemplate { Body = "{Name}" }, recipients);

            Assert.That(fake.BulkSent.Single().Recipients[0].Variables["Name"], Is.EqualTo("<b>"));
        }

        [Test]
        public async Task SendBulk_Redirect時は先頭10通だけ個別送信され総数はヘッダに残る()
        {
            var (dispatcher, fake) = Create(redirectAllTo: "catch@example.com");
            var recipients = Enumerable.Range(0, 25).Select(i => new MailBulkRecipient
            {
                To = $"user{i}@example.com",
                Variables = { ["No"] = i.ToString() },
            }).ToList();

            var result = await dispatcher.SendBulkAsync(null, new MailBulkTemplate { Subject = "no.{No}", Body = "b" }, recipients);

            Assert.That(fake.BulkSent, Is.Empty); //バルクは使わない
            Assert.That(fake.Sent, Has.Count.EqualTo(10));
            Assert.That(fake.Sent.All(e => e.To.Single() == "catch@example.com"), Is.True);
            Assert.That(fake.Sent[0].Subject, Is.EqualTo("no.0")); //差し込みは解決済み
            Assert.That(fake.Sent[0].Headers[MailDispatcher.OriginalToHeader], Does.Contain("user0@example.com"));
            Assert.That(fake.Sent[0].Headers[MailDispatcher.OriginalTotalHeader], Is.EqualTo("25"));
            Assert.That(result.TotalCount, Is.EqualTo(25));
            Assert.That(result.SuccessCount, Is.EqualTo(25));
        }

        [Test]
        public async Task カスタムセンダーファクトリが優先される()
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                Senders = { new MailSenderSettings { Name = "Custom", Type = "MyGateway" } }
            };
            var dispatcher = new MailDispatcher(config, s => s.Type == "MyGateway" ? fake : null);

            var result = await dispatcher.SendAsync("Custom", new MailMessage { To = { "a@example.com" } });
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fake.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public void 不明なセンダータイプはエラー()
        {
            var dispatcher = new MailDispatcher(new MailConfig
            {
                Senders = { new MailSenderSettings { Name = "X", Type = "Unknown" } }
            });
            Assert.That(async () => await dispatcher.SendAsync("X", new MailMessage { To = { "a@example.com" } }),
                Throws.InvalidOperationException);
        }
    }
}
