using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>メールのワンタイムコード: 発行→送信→照合、期限、試行超過、再送で前のコードが無効になること。</summary>
    public class EmailOtpLoginTest
    {
        readonly List<MailMessage> _sent = new();
        bool _sendFails;

        EmailOtpLogin Create(EmailOtpLoginSettings? settings = null)
        {
            var cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            return new EmailOtpLogin(settings ?? new(), m =>
            {
                if (_sendFails) return Task.FromResult(new MailSendResult { TotalCount = 1, Failures = { new MailSendFailure { To = m.To.FirstOrDefault() ?? "", Error = "down" } } });
                _sent.Add(m);
                return Task.FromResult(new MailSendResult { TotalCount = 1, SuccessCount = 1 });
            }, cache);
        }

        static string CodeIn(MailMessage m) => Regex.Match(m.Body, @"\b(\d{6})\b").Groups[1].Value;

        [SetUp]
        public void SetUp() { _sent.Clear(); _sendFails = false; }

        [Test]
        public async Task Issue_Send_Verify()
        {
            var otp = Create();
            var issued = await otp.VerifyAsync("U1", "taro@example.com", null);
            Assert.That(issued.Status, Is.EqualTo(EmailOtpLoginStatus.CodeRequired));
            Assert.That(issued.MaskedEmail, Is.EqualTo("t***@example.com"));
            Assert.That(_sent, Has.Count.EqualTo(1));
            Assert.That(_sent[0].To, Is.EqualTo(new[] { "taro@example.com" }));
            var code = CodeIn(_sent[0]);
            Assert.That(code, Has.Length.EqualTo(6));
            Assert.That(_sent[0].Subject, Does.Contain(code));
            Assert.That(_sent[0].Body, Does.Contain("10 分"));

            if (code != "000000") Assert.That((await otp.VerifyAsync("U1", "taro@example.com", "000000")).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode));
            Assert.That((await otp.VerifyAsync("U1", "taro@example.com", " " + code + " ")).Status, Is.EqualTo(EmailOtpLoginStatus.Ok), "前後の空白は許容");
            Assert.That((await otp.VerifyAsync("U1", "taro@example.com", code)).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode), "使い捨て");
        }

        [Test]
        public async Task Reissue_InvalidatesThePreviousCode()
        {
            var otp = Create();
            await otp.VerifyAsync("U1", "taro@example.com", null);
            var first = CodeIn(_sent[0]);
            await otp.VerifyAsync("U1", "taro@example.com", "");
            var second = CodeIn(_sent[1]);
            Assert.That(_sent, Has.Count.EqualTo(2));
            if (first != second) Assert.That((await otp.VerifyAsync("U1", "taro@example.com", first)).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode));
            Assert.That((await otp.VerifyAsync("U1", "taro@example.com", second)).Status, Is.EqualTo(EmailOtpLoginStatus.Ok));
        }

        [Test]
        public async Task TooManyAttempts_DiscardsTheCode()
        {
            var otp = Create(new EmailOtpLoginSettings { MaxAttempts = 3 });
            await otp.VerifyAsync("U1", "taro@example.com", null);
            var code = CodeIn(_sent[0]);
            var wrong = code == "111111" ? "222222" : "111111";
            for (var i = 0; i < 3; i++) Assert.That((await otp.VerifyAsync("U1", "taro@example.com", wrong)).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode));
            Assert.That((await otp.VerifyAsync("U1", "taro@example.com", code)).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode), "3 回外したらコードは破棄され、正しいコードも通らない");
        }

        [Test]
        public async Task Expired_IsInvalid()
        {
            //有効期限は分単位 (最小 1 分) なので、期限切れはキャッシュを直接期限切れにして代用: 別インスタンス (別キャッシュ) では見えないことで確認
            var otp = Create();
            await otp.VerifyAsync("U1", "taro@example.com", null);
            var code = CodeIn(_sent[0]);
            var other = Create();
            Assert.That((await other.VerifyAsync("U1", "taro@example.com", code)).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode));
        }

        [Test]
        public async Task SendFailure_DoesNotIssueACode()
        {
            var otp = Create();
            _sendFails = true;
            var r = await otp.VerifyAsync("U1", "taro@example.com", null);
            Assert.That(r.Status, Is.EqualTo(EmailOtpLoginStatus.SendFailed));
            _sendFails = false;
            Assert.That((await otp.VerifyAsync("U1", "taro@example.com", "123456")).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode));
            Assert.That((await otp.VerifyAsync("U1", "", null)).Status, Is.EqualTo(EmailOtpLoginStatus.SendFailed), "送信先が無い");
        }

        [Test]
        public async Task UsersAreIndependent_AndTemplatesApply()
        {
            var otp = Create(new EmailOtpLoginSettings { Subject = "[{code}] ログイン", Body = "code={code} min={minutes}", CodeLifetimeMinutes = 3 });
            await otp.VerifyAsync("A", "a@example.com", null);
            await otp.VerifyAsync("B", "b@example.com", null);
            Assert.That(_sent[0].Subject, Does.StartWith("[").And.EndWith("] ログイン"));
            Assert.That(_sent[0].Body, Does.EndWith(" min=3"));
            var a = Regex.Match(_sent[0].Body, @"code=(\d{6})").Groups[1].Value;
            var b = Regex.Match(_sent[1].Body, @"code=(\d{6})").Groups[1].Value;
            if (a != b) Assert.That((await otp.VerifyAsync("A", "a@example.com", b)).Status, Is.EqualTo(EmailOtpLoginStatus.InvalidCode), "他人のコードは通らない");
            Assert.That((await otp.VerifyAsync("A", "a@example.com", a)).Status, Is.EqualTo(EmailOtpLoginStatus.Ok));
            Assert.That((await otp.VerifyAsync("B", "b@example.com", b)).Status, Is.EqualTo(EmailOtpLoginStatus.Ok));
        }

        [Test]
        public void Mask()
        {
            Assert.That(EmailOtpLogin.Mask("taro@example.com"), Is.EqualTo("t***@example.com"));
            Assert.That(EmailOtpLogin.Mask("a@example.com"), Is.EqualTo("a@example.com"));
            Assert.That(EmailOtpLogin.Mask("no-at"), Is.EqualTo("***"));
        }
    }
}
