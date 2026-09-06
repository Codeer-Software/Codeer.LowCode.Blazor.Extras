using Codeer.LowCode.Blazor.Extras.Server.Auth;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>RFC 6238 のテストベクタ (付録 B、HMAC-SHA1) と Base32・照合窓・otpauth URI。</summary>
    public class TotpTest
    {
        //RFC 6238 の共通鍵 "12345678901234567890" (ASCII 20 バイト) を Base32 にしたもの
        const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

        [TestCase(59L, "287082")]
        [TestCase(1111111109L, "081804")]
        [TestCase(1111111111L, "050471")]
        [TestCase(1234567890L, "005924")]
        [TestCase(2000000000L, "279037")]
        [TestCase(20000000000L, "353130")]
        public void Rfc6238Vectors(long unixTime, string expected6Digits)
        {
            //RFC の表は 8 桁。6 桁はその下 6 桁
            Assert.That(Totp.ComputeCode(RfcSecret, unixTime / Totp.PeriodSeconds), Is.EqualTo(expected6Digits));
            Assert.That(Totp.VerifyCode(RfcSecret, expected6Digits, DateTimeOffset.FromUnixTimeSeconds(unixTime)), Is.EqualTo(unixTime / Totp.PeriodSeconds));
        }

        [Test]
        public void Base32_RoundTrip_AndRfcKey()
        {
            Assert.That(Totp.Base32Encode(Encoding.ASCII.GetBytes("12345678901234567890")), Is.EqualTo(RfcSecret));
            Assert.That(Encoding.ASCII.GetString(Totp.Base32Decode(RfcSecret)), Is.EqualTo("12345678901234567890"));
            Assert.That(Totp.Base32Decode("gezd gnbv-gy3tqojqgezdgnbvgy3tqojq===="), Is.EqualTo(Encoding.ASCII.GetBytes("12345678901234567890")), "小文字・空白・ハイフン・パディングを許容");
            Assert.Throws<FormatException>(() => Totp.Base32Decode("ABC1"));

            var secret = Totp.CreateSecret();
            Assert.That(secret.Length, Is.EqualTo(32), "160 ビット = Base32 で 32 文字");
            Assert.That(Totp.Base32Decode(secret).Length, Is.EqualTo(20));
            Assert.That(Totp.CreateSecret(), Is.Not.EqualTo(secret));
        }

        [Test]
        public void VerifyCode_AcceptsPlusMinusOneStep_RejectsFurther()
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
            var step = 1234567890 / Totp.PeriodSeconds;
            Assert.That(Totp.VerifyCode(RfcSecret, Totp.ComputeCode(RfcSecret, step - 1), now), Is.EqualTo(step - 1));
            Assert.That(Totp.VerifyCode(RfcSecret, Totp.ComputeCode(RfcSecret, step + 1), now), Is.EqualTo(step + 1));
            Assert.That(Totp.VerifyCode(RfcSecret, Totp.ComputeCode(RfcSecret, step - 2), now), Is.Null);
            Assert.That(Totp.VerifyCode(RfcSecret, Totp.ComputeCode(RfcSecret, step + 2), now), Is.Null);
        }

        [Test]
        public void VerifyCode_RejectsMalformedInput()
        {
            var now = DateTimeOffset.FromUnixTimeSeconds(59);
            Assert.That(Totp.VerifyCode(RfcSecret, "28708", now), Is.Null, "5 桁");
            Assert.That(Totp.VerifyCode(RfcSecret, "2870821", now), Is.Null, "7 桁");
            Assert.That(Totp.VerifyCode(RfcSecret, "28708a", now), Is.Null, "数字以外");
            Assert.That(Totp.VerifyCode("not-base32-1", "287082", now), Is.Null, "壊れた鍵は例外にせず不一致");
        }

        [Test]
        public void OtpauthUri_EscapesIssuerAndAccount()
        {
            var uri = Totp.CreateOtpauthUri("My App", "taro@example.com", RfcSecret);
            Assert.That(uri, Is.EqualTo($"otpauth://totp/My%20App:taro%40example.com?secret={RfcSecret}&issuer=My%20App&algorithm=SHA1&digits=6&period=30"));
        }
    }
}
