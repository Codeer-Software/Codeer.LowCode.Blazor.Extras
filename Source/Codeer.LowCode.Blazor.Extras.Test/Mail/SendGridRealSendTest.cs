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
    /// SendGridMailSender の実送信確認。SendGrid アカウントと API キー (Mail Send 権限) が必要。
    /// 差出人は SendGrid 側で検証済みであること (Single Sender Verification またはドメイン認証。未検証だと 403)。
    /// 設定はリポジトリ外の JSON ファイルから読む (既定 C:\Codeer.LowCode.Blazor.Local\sendgrid_mail_test.json、
    /// 環境変数 CLB_SENDGRID_MAIL_TEST でパス変更可)。ファイルが無ければ Ignore。
    /// <code>
    /// {
    ///   "SenderMailAddress": "info@example.com",
    ///   "SenderDisplayName": "CLB SendGrid Test",
    ///   "ApiKey": "SG...."
    /// }
    /// </code>
    /// 宛先は差出人自身 (自分宛て送信)。到着確認は差出人の受信箱を目視。
    /// バルクの差し込みは SendGrid 側の substitutions 置換なので実機での確認が必須。
    /// </summary>
    [Explicit("requires a SendGrid account (API key + verified sender) and a real mailbox")]
    public class SendGridRealSendTest
    {
        static readonly string SettingsPath = Environment.GetEnvironmentVariable("CLB_SENDGRID_MAIL_TEST")
            ?? @"C:\Codeer.LowCode.Blazor.Local\sendgrid_mail_test.json";

        SendGridSettings _settings = null!;
        string _testTo = null!;
        string _stamp = null!;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(SettingsPath)) Assert.Ignore($"settings file not found: {SettingsPath}");
            _settings = JsonSerializer.Deserialize<SendGridSettings>(File.ReadAllText(SettingsPath))!;
            if (string.IsNullOrEmpty(_settings.ApiKey)) Assert.Ignore("ApiKey is empty");
            _testTo = _settings.SenderMailAddress;
            _stamp = DateTime.Now.ToString("HH:mm:ss");
        }

        [Test]
        public async Task 単発_HTML_添付_ReplyTo_ヘッダ()
        {
            var sender = new SendGridMailSender(_settings);
            var result = await sender.SendAsync(new MailMessage
            {
                To = { _testTo },
                //Cc は付けない (自分宛て送信のため To と重複し、SendGrid は personalization 内の重複アドレスを 400 で拒否する)
                Subject = $"[CLB SendGrid] 単発テスト {_stamp}",
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
            var sender = new SendGridMailSender(_settings);
            var result = await sender.SendAsync(new MailMessage
            {
                To = { _testTo },
                Subject = $"[CLB SendGrid] plain {_stamp}",
                Body = "line1\r\nline2",
            });
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(f => f.Error)));
        }

        [Test]
        public async Task バルク_3通_差し込みはSendGrid側のsubstitutionsで置換される()
        {
            var sender = new SendGridMailSender(_settings);
            var template = new MailBulkTemplate
            {
                Subject = $"[CLB SendGrid] {{Name}} 様へのお知らせ {_stamp}",
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
            //到着した3通の件名・本文で {Name}/{Amount} が置換済みであることを目視確認する
        }

        [Test]
        public async Task 認証失敗は例外ではなく失敗結果()
        {
            var bad = new SendGridSettings
            {
                ApiKey = "SG.invalid",
                SenderMailAddress = _settings.SenderMailAddress,
            };
            var result = await new SendGridMailSender(bad).SendAsync(new MailMessage { To = { _testTo }, Subject = "x", Body = "y" });
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures.Single().Error, Does.Contain("401"));
        }
    }
}
