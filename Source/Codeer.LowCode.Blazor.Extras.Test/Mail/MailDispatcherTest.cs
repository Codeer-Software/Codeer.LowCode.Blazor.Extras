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

        static (MailDispatcher dispatcher, FakeMailSender fake) Create(string debugRedirectAllTo = "", int maxBulkCount = 10000)
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                DebugRedirectAllTo = debugRedirectAllTo,
                Infras =
                {
                    new MailInfraSettings { Name = "Main", Type = MailInfraTypes.Smtp, MaxBulkCount = maxBulkCount },
                    new MailInfraSettings { Name = "Sub", Type = MailInfraTypes.SendGrid },
                }
            };
            return (new MailDispatcher(config, _ => fake), fake);
        }

        [Test]
        public void ResolveInfraSettings_省略は先頭_名前指定は一致_不明はエラー()
        {
            var (dispatcher, _) = Create();
            Assert.That(dispatcher.ResolveInfraSettings(null).Name, Is.EqualTo("Main"));
            Assert.That(dispatcher.ResolveInfraSettings("Sub").Name, Is.EqualTo("Sub"));
            Assert.That(() => dispatcher.ResolveInfraSettings("Nothing"), Throws.InvalidOperationException);
        }

        [Test]
        public void ResolveInfraSettings_設定なしはエラー()
        {
            var dispatcher = new MailDispatcher(new MailConfig());
            Assert.That(() => dispatcher.ResolveInfraSettings(null), Throws.InvalidOperationException);
        }

        static MailDispatcher CreateWithDefaults(string defaultSender, string defaultBulkSender)
            => new(new MailConfig
            {
                DefaultInfraName = defaultSender,
                DefaultBulkInfraName = defaultBulkSender,
                Infras =
                {
                    new MailInfraSettings { Name = "Main" },
                    new MailInfraSettings { Name = "Notify" },
                    new MailInfraSettings { Name = "Campaign" },
                }
            });

        [Test]
        public void ResolveInfraSettings_省略時は用途別デフォルト_明示指定が最優先()
        {
            var dispatcher = CreateWithDefaults("Notify", "Campaign");
            Assert.That(dispatcher.ResolveInfraSettings(null).Name, Is.EqualTo("Notify"));
            Assert.That(dispatcher.ResolveBulkInfraSettings(null).Name, Is.EqualTo("Campaign"));
            Assert.That(dispatcher.ResolveInfraSettings("Campaign").Name, Is.EqualTo("Campaign"));
            Assert.That(dispatcher.ResolveBulkInfraSettings("Notify").Name, Is.EqualTo("Notify"));
        }

        [Test]
        public void ResolveBulkInfraSettings_Bulk既定なしは単発既定_両方なしは先頭()
        {
            Assert.That(CreateWithDefaults("Notify", "").ResolveBulkInfraSettings(null).Name, Is.EqualTo("Notify"));
            Assert.That(CreateWithDefaults("", "").ResolveBulkInfraSettings(null).Name, Is.EqualTo("Main"));
        }

        [Test]
        public void ResolveInfraSettings_デフォルト名が不明でも黙って先頭に落ちずエラー()
        {
            Assert.That(() => CreateWithDefaults("Nothing", "").ResolveInfraSettings(null), Throws.InvalidOperationException);
            Assert.That(() => CreateWithDefaults("", "Nothing").ResolveBulkInfraSettings(null), Throws.InvalidOperationException);
        }

        [Test]
        public void SendBulk_センダー省略時はBulk既定のセンダーで送られる()
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                DefaultInfraName = "Notify",
                DefaultBulkInfraName = "Campaign",
                Infras =
                {
                    new MailInfraSettings { Name = "Notify" },
                    new MailInfraSettings { Name = "Campaign", MaxBulkCount = 1 },
                }
            };
            var dispatcher = new MailDispatcher(config, _ => fake);

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
        public async Task カスタムセンダーファクトリが優先される()
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                Infras = { new MailInfraSettings { Name = "Custom", Type = "MyGateway" } }
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
                Infras = { new MailInfraSettings { Name = "X", Type = "Unknown" } }
            });
            Assert.That(async () => await dispatcher.SendAsync("X", new MailMessage { To = { "a@example.com" } }),
                Throws.InvalidOperationException);
        }

        //================= 差出人 (IsFromCurrentUser = 自分を差出人にする) =================

        static (MailDispatcher dispatcher, FakeMailSender fake) CreateWithCurrentUser(MailCurrentUser? user)
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                Infras = { new MailInfraSettings { Name = "Main", Type = MailInfraTypes.Smtp } },
            };
            return (new MailDispatcher(config, _ => fake, currentUserResolver: () => Task.FromResult(user)), fake);
        }

        [Test]
        public async Task From_フラグONでサーバーが解決した操作ユーザーになる()
        {
            var (dispatcher, fake) = CreateWithCurrentUser(new MailCurrentUser { Email = "sales@example.com", DisplayName = "営業 太郎" });
            var result = await dispatcher.SendAsync(new MailSendRequest
            {
                IsFromCurrentUser = true,
                Message = new MailMessage { To = { "to@example.com" }, Subject = "s" },
            });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fake.Sent.Single().From, Is.EqualTo("sales@example.com"));
            Assert.That(fake.Sent.Single().FromDisplayName, Is.EqualTo("営業 太郎"));
        }

        [Test]
        public async Task From_クライアントが載せたFromは無視される()
        {
            //ワイヤ経由の From はなりすまし防止のため常に破棄される (フラグOFF = システム差出人)
            var (dispatcher, fake) = CreateWithCurrentUser(new MailCurrentUser { Email = "sales@example.com" });
            var result = await dispatcher.SendAsync(new MailSendRequest
            {
                Message = new MailMessage { From = "spoof@evil.com", FromDisplayName = "偽", To = { "to@example.com" }, Subject = "s" },
            });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fake.Sent.Single().From, Is.Empty);
            Assert.That(fake.Sent.Single().FromDisplayName, Is.Empty);
        }

        [Test]
        public async Task From_フラグONで操作ユーザーが解決できなければ失敗()
        {
            var (dispatcher, fake) = CreateWithCurrentUser(null);
            var result = await dispatcher.SendAsync(new MailSendRequest
            {
                IsFromCurrentUser = true,
                Message = new MailMessage { To = { "to@example.com" }, Subject = "s" },
            });

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures.Single().Error, Does.Contain("IsFromCurrentUser"));
            Assert.That(fake.Sent, Is.Empty);
        }

        [Test]
        public async Task From_リダイレクト時も維持される()
        {
            var fake = new FakeMailSender();
            var config = new MailConfig
            {
                DebugRedirectAllTo = "test@example.com",
                Infras = { new MailInfraSettings { Name = "Main" } },
            };
            var dispatcher = new MailDispatcher(config, _ => fake,
                currentUserResolver: () => Task.FromResult<MailCurrentUser?>(new() { Email = "sales@example.com" }));

            var result = await dispatcher.SendAsync(new MailSendRequest
            {
                IsFromCurrentUser = true,
                Message = new MailMessage { To = { "customer@other.com" }, Subject = "s" },
            });

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(fake.Sent.Single().To, Is.EqualTo(new[] { "test@example.com" }));
            Assert.That(fake.Sent.Single().From, Is.EqualTo("sales@example.com"));
        }
    }
}
