using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// GmailApiMailSender の実送信確認 (一斉送信が件数どおり届くか)。Gmail API の認証情報が必要。
    /// 設定はリポジトリ外の JSON ファイルから読む (既定 C:\Codeer.LowCode.Blazor.Local\gmail_mail_test.json、
    /// 環境変数 CLB_GMAIL_MAIL_TEST でパス変更可)。ファイルが無ければ Ignore。
    /// <code>
    /// {
    ///   "SenderMailAddress": "me@example.com",
    ///   "SenderDisplayName": "CLB Gmail Test",
    ///   "ClientSecret": "C:\\...\\client_secret.json",   // GmailSettings と同じ (パスか JSON 文字列)
    ///   "TokenSecret": "C:\\...\\token.json",
    ///   "MaxBulkCount": 20,
    ///   "TestTo": "me+{0}@example.com"                   // {0} に連番が入る (プラスアドレスで自分の受信箱に集める)
    /// }
    /// </code>
    /// 到着確認は受信箱を目視 (件名に連番と時刻)。
    /// </summary>
    [Explicit("requires Gmail API credentials and a real mailbox")]
    public class GmailApiRealSendTest
    {
        static readonly string SettingsPath = Environment.GetEnvironmentVariable("CLB_GMAIL_MAIL_TEST")
            ?? @"C:\Codeer.LowCode.Blazor.Local\gmail_mail_test.json";

        GmailSettings _settings = null!;
        string _testTo = null!;
        string _stamp = null!;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(SettingsPath)) Assert.Ignore($"settings file not found: {SettingsPath}");
            var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(SettingsPath));
            _settings = JsonSerializer.Deserialize<GmailSettings>(json.GetRawText())!;
            _testTo = json.GetProperty("TestTo").GetString()!;
            _stamp = DateTime.Now.ToString("HH:mm:ss");
        }

        [Test]
        public async Task 一斉送信_12件が全部送られる()
        {
            //製品と同じ経路 (MailDispatcher → GmailApiMailSender)。DebugRedirectAllTo は空 = 転送も切り詰めも無し
            var sender = new GmailApiMailSender(_settings);
            var dispatcher = new MailDispatcher(new MailConfig { DefaultInfraName = "Gmail" }, name => name == "Gmail" ? sender : null);
            var recipients = Enumerable.Range(1, 12).Select(i => new MailBulkRecipient
            {
                To = string.Format(_testTo, $"bulk{i:00}"),
                Variables = { ["No"] = i.ToString() },
            }).ToList();

            var result = await dispatcher.SendBulkAsync("Gmail",
                new MailBulkTemplate { Subject = $"[CLB Gmail] 一斉送信テスト {{No}}/12 {_stamp}", Body = "No {No}" },
                recipients);

            Assert.That(result.Failures.Select(f => $"{f.To}: {f.Error}"), Is.Empty);
            Assert.That(result.SuccessCount, Is.EqualTo(12));
            Assert.That(result.TotalCount, Is.EqualTo(12));
        }
    }
}
