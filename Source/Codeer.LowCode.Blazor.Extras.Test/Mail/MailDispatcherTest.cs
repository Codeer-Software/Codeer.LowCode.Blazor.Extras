using Codeer.LowCode.Blazor.Extras.ScriptObjects;
//ScriptObjects には旧 API (0.5.0 互換) の MailMessage / MailAttachment もあるので、こちらは新 API の型を使う
using MailAttachment = Codeer.LowCode.Blazor.Extras.Mail.MailAttachment;
using MailMessage = Codeer.LowCode.Blazor.Extras.Mail.MailMessage;
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

            public int MaxBulkCount { get; set; } = 10000;

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

        static (MailDispatcher dispatcher, FakeMailSender fake) Create(string debugRedirectAllTo = "", int maxBulkCount = 10000)
        {
            var fake = new FakeMailSender { MaxBulkCount = maxBulkCount };
            //呼び名の省略は appsettings の既定で解決する (空のまま対応表に渡ることはない)
            var config = new MailConfig { DebugRedirectAllTo = debugRedirectAllTo, DefaultInfraName = "Main" };
            //テンプレートの対応表に相当
            return (new MailDispatcher(config, name => name == "Main" ? fake : null), fake);
        }

        [Test]
        public void ResolveInfraName_省略は既定名_明示指定が最優先()
        {
            var dispatcher = new MailDispatcher(
                new MailConfig { DefaultInfraName = "Notify", DefaultBulkInfraName = "Campaign" }, _ => null);

            Assert.That(dispatcher.ResolveInfraName(null), Is.EqualTo("Notify"));
            Assert.That(dispatcher.ResolveBulkInfraName(null), Is.EqualTo("Campaign"));
            Assert.That(dispatcher.ResolveInfraName("Other"), Is.EqualTo("Other"));
            Assert.That(dispatcher.ResolveBulkInfraName("Other"), Is.EqualTo("Other"));
        }

        [Test]
        public void ResolveBulkInfraName_Bulk既定なしは単発既定_両方なしは空()
        {
            Assert.That(new MailDispatcher(new MailConfig { DefaultInfraName = "Notify" }, _ => null)
                .ResolveBulkInfraName(null), Is.EqualTo("Notify"));
            Assert.That(new MailDispatcher(new MailConfig(), _ => null).ResolveBulkInfraName(null), Is.Empty);
        }

        [Test]
        public void CreateSender_対応表が知らない呼び名はエラー()
        {
            var (dispatcher, _) = Create();
            Assert.That(() => dispatcher.CreateSender("Nothing"),
                Throws.InvalidOperationException.With.Message.Contains("Nothing"));
        }

        [Test]
        public void CreateSender_呼び名が空なら対応表を呼ばずにエラー()
        {
            //設定ミスを黙って別のインフラで送らない (以前の「先頭に落とす」挙動は廃止)。
            //対応表が空を引き受けても (旧テンプレの "Smtp" or "") それには従わない =
            //設定漏れが別インフラのエラーに化けない
            var called = new List<string>();
            var dispatcher = new MailDispatcher(new MailConfig(), name =>
            {
                called.Add(name);
                return new FakeMailSender();
            });

            Assert.That(() => dispatcher.CreateSender(string.Empty),
                Throws.InvalidOperationException.With.Message.Contains("Mail.DefaultInfraName"));
            Assert.That(called, Is.Empty);
        }

        [Test]
        public void SendBulk_センダー省略時はBulk既定のセンダーで送られる()
        {
            var notify = new FakeMailSender();
            var campaign = new FakeMailSender { MaxBulkCount = 1 };
            var config = new MailConfig { DefaultInfraName = "Notify", DefaultBulkInfraName = "Campaign" };
            var dispatcher = new MailDispatcher(config, name => name switch
            {
                "Notify" => notify,
                "Campaign" => campaign,
                _ => null,
            });

            //Campaign(MaxBulkCount=1)が選ばれている証拠として2件で超過エラーになる
            Assert.That(async () => await dispatcher.SendBulkAsync(null, new MailBulkTemplate(),
                [new MailBulkRecipient { To = "a@example.com" }, new MailBulkRecipient { To = "b@example.com" }]),
                Throws.InvalidOperationException.With.Message.Contains("Campaign"));
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
            var (dispatcher, fake) = Create(debugRedirectAllTo: "catch@example.com");
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
            var (dispatcher, fake) = Create(debugRedirectAllTo: "catch@example.com");
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
        public async Task 独自インフラも対応表に足すだけで送れる()
        {
            var fake = new FakeMailSender();
            var dispatcher = new MailDispatcher(new MailConfig(), name => name == "MyGateway" ? fake : null);

            var result = await dispatcher.SendAsync("MyGateway", new MailMessage { To = { "a@example.com" } });
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fake.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task 対応表が知らない呼び名で送ると呼び名がわかる失敗になる()
        {
            var dispatcher = new MailDispatcher(new MailConfig(), _ => null);
            var result = await dispatcher.SendAsync("X", new MailMessage { To = { "a@example.com" } });

            Assert.That(result.IsSuccess, Is.False);
            //スクリプトの戻り値・トーストに出るので、名前と直し場所が文言に入っている
            Assert.That(result.Failures.Single().Error, Does.Contain("'X'").And.Contain("MailSenderTable"));
        }

        [Test]
        public async Task 呼び名も既定も空で送ると設定漏れがわかる失敗になる()
        {
            //対応表が空を引き受ける実装でも、空は対応表に渡らない (SMTP未設定などの別エラーに化けない)
            var dispatcher = new MailDispatcher(new MailConfig(), _ => new FakeMailSender());
            var result = await dispatcher.SendAsync(null, new MailMessage { To = { "a@example.com" } });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures.Single().To, Is.EqualTo("a@example.com"));
            Assert.That(result.Failures.Single().Error,
                Does.Contain("MailInfraName").And.Contain("Mail.DefaultInfraName"));
        }

        [Test]
        public async Task SendBulk_呼び名も既定も空なら宛先ごとに設定漏れの失敗になる()
        {
            var dispatcher = new MailDispatcher(new MailConfig(), _ => new FakeMailSender());
            var recipients = Enumerable.Range(0, 3).Select(i => new MailBulkRecipient { To = $"user{i}@example.com" }).ToList();
            var result = await dispatcher.SendBulkAsync(null, new MailBulkTemplate(), recipients);

            Assert.That(result.TotalCount, Is.EqualTo(3));
            Assert.That(result.SuccessCount, Is.Zero);
            Assert.That(result.Failures.Select(e => e.To),
                Is.EqualTo(new[] { "user0@example.com", "user1@example.com", "user2@example.com" }));
            Assert.That(result.Failures[0].Error, Does.Contain("Mail.DefaultBulkInfraName"));
        }

        [Test]
        public async Task SendBulk_宛先0件でも呼び名未指定は成功にしない()
        {
            var dispatcher = new MailDispatcher(new MailConfig(), _ => new FakeMailSender());
            var result = await dispatcher.SendBulkAsync(null, new MailBulkTemplate(), new List<MailBulkRecipient>());

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures.Single().Error, Does.Contain("Mail.DefaultInfraName"));
        }

        //================= 差出人 =================

        [Test]
        public async Task From_クライアントが載せたFromは無視される()
        {
            //ワイヤ経由の From はなりすまし防止のため常に破棄される (差出人は送信インフラ設定のシステム送信者)
            var fake = new FakeMailSender();
            var dispatcher = new MailDispatcher(new MailConfig { DefaultInfraName = "Main" }, _ => fake);
            var result = await dispatcher.SendAsync(new MailSendRequest
            {
                Message = new MailMessage { From = "spoof@evil.com", FromDisplayName = "偽", To = { "to@example.com" }, Subject = "s" },
            });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fake.Sent.Single().From, Is.Empty);
            Assert.That(fake.Sent.Single().FromDisplayName, Is.Empty);
        }
    }
}
