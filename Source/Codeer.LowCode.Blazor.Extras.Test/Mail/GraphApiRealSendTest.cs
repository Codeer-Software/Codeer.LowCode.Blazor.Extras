using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
//ScriptObjects には旧 API (0.5.0 互換) の MailMessage / MailAttachment もあるので、こちらは新 API の型を使う
using MailAttachment = Codeer.LowCode.Blazor.Extras.Mail.MailAttachment;
using MailMessage = Codeer.LowCode.Blazor.Extras.Mail.MailMessage;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// GraphApiMailSender の実送信確認。Entra ID アプリ登録 (アプリケーション権限 Mail.Send + 管理者同意) が必要。
    /// 設定はリポジトリ外の JSON ファイルから読む (既定 C:\Codeer.LowCode.Blazor.Local\graph_mail_test.json、
    /// 環境変数 CLB_GRAPH_MAIL_TEST でパス変更可)。ファイルが無ければ Ignore。
    /// <code>
    /// {
    ///   "SenderMailAddress": "info@example.com",
    ///   "SenderDisplayName": "CLB Graph Test",
    ///   "TenantId": "...",
    ///   "ClientId": "...",
    ///   "ClientSecret": "...",
    ///   "TestTo": "someone@example.com"
    /// }
    /// </code>
    /// 到着確認は受信箱 (TestTo) と差出人の送信済みアイテム (saveToSentItems:true) を目視。
    /// </summary>
    [Explicit("requires Entra ID app registration and a real mailbox")]
    public class GraphApiRealSendTest
    {
        static readonly string SettingsPath = Environment.GetEnvironmentVariable("CLB_GRAPH_MAIL_TEST")
            ?? @"C:\Codeer.LowCode.Blazor.Local\graph_mail_test.json";

        GraphApiSettings _settings = null!;
        string _testTo = null!;
        string _stamp = null!;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(SettingsPath)) Assert.Ignore($"settings file not found: {SettingsPath}");
            var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(SettingsPath));
            _settings = JsonSerializer.Deserialize<GraphApiSettings>(json.GetRawText())!;
            _testTo = json.GetProperty("TestTo").GetString()!;
            _stamp = DateTime.Now.ToString("HH:mm:ss");
        }

        [Test]
        public async Task 単発_HTML_添付_ReplyTo_ヘッダ()
        {
            var sender = new GraphApiMailSender(_settings);
            var result = await sender.SendAsync(new MailMessage
            {
                To = { _testTo },
                Cc = { _settings.SenderMailAddress },
                Subject = $"[CLB Graph] 単発テスト {_stamp}",
                Body = "<p>本文 <b>太字</b> 日本語</p>",
                IsBodyHtml = true,
                ReplyTo = _settings.SenderMailAddress,
                Attachments = { new MailAttachment { FileName = "a.txt", ContentBase64 = Convert.ToBase64String("ABC"u8.ToArray()) } },
                Headers = { ["X-CLB-Original-To"] = "orig@example.com" },
            });
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(f => f.Error)));
            Assert.That(result.SuccessCount, Is.EqualTo(1));
        }

        [Test]
        public async Task 単発_プレーンテキスト()
        {
            var sender = new GraphApiMailSender(_settings);
            var result = await sender.SendAsync(new MailMessage
            {
                To = { _testTo },
                Subject = $"[CLB Graph] plain {_stamp}",
                Body = "line1\r\nline2",
            });
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(f => f.Error)));
        }

        [Test]
        public async Task バルク_3通_差し込み()
        {
            var sender = new GraphApiMailSender(_settings);
            var template = new MailBulkTemplate
            {
                Subject = $"[CLB Graph] {{Name}} 様へのお知らせ {_stamp}",
                Body = "{Name} 様\r\n残高は {Amount} 円です。",
            };
            var recipients = Enumerable.Range(1, 3).Select(i => new MailBulkRecipient
            {
                To = _testTo,
                Variables = { ["Name"] = $"ユーザー{i}", ["Amount"] = (i * 1000).ToString() },
            }).ToList();

            var result = await sender.SendBulkAsync(template, recipients);
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(f => f.Error)));
            Assert.That(result.SuccessCount, Is.EqualTo(3));
        }

        [Test]
        public async Task 認証失敗は例外ではなく失敗結果()
        {
            var bad = new GraphApiSettings
            {
                TenantId = _settings.TenantId,
                ClientId = _settings.ClientId,
                ClientSecret = "invalid-secret",
                SenderMailAddress = _settings.SenderMailAddress,
            };
            var result = await new GraphApiMailSender(bad).SendAsync(new MailMessage { To = { _testTo }, Subject = "x", Body = "y" });
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures.Single().Error, Does.Contain("token"));
        }
    }
}
