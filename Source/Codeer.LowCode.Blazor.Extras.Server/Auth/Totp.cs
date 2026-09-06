using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// RFC 6238 TOTP (HMAC-SHA1 / 30 秒 / 6 桁)。Google Authenticator / Microsoft Authenticator 等の既定パラメータに合わせている。
    /// 秘密鍵は Base32 文字列で扱う (otpauth URI にそのまま載せる形式)。
    /// </summary>
    public static class Totp
    {
        public const int PeriodSeconds = 30;
        public const int Digits = 6;
        const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        /// <summary>160 ビットの秘密鍵を新しく作る (Base32)。</summary>
        public static string CreateSecret()
            => Base32Encode(RandomNumberGenerator.GetBytes(20));

        /// <summary>オーセンティケータアプリに登録する URI (QR コードの中身)。issuer はアプリ名、account はユーザー名。</summary>
        public static string CreateOtpauthUri(string issuer, string account, string secret)
            => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}" +
               $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";

        /// <summary>
        /// 現在時刻 ±1 ステップでコードを照合し、一致したタイムステップを返す (不一致は null)。
        /// リプレイ防止のため、呼び出し側は前回成功したステップより大きいことを確認してから受け入れる (<see cref="TotpLogin"/> がそうしている)。
        /// </summary>
        public static long? VerifyCode(string secret, string code) => VerifyCode(secret, code, DateTimeOffset.UtcNow);

        public static long? VerifyCode(string secret, string code, DateTimeOffset now)
        {
            if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;

            byte[] key;
            try
            {
                key = Base32Decode(secret);
            }
            catch
            {
                return null;
            }

            var currentStep = now.ToUnixTimeSeconds() / PeriodSeconds;
            for (var offset = -1; offset <= 1; offset++)
            {
                var step = currentStep + offset;
                if (ComputeCode(key, step) == code) return step;
            }
            return null;
        }

        /// <summary>タイムステップ (Unix 秒 / 30) に対応する 6 桁コード。テストと検証用。</summary>
        public static string ComputeCode(string secret, long timestep) => ComputeCode(Base32Decode(secret), timestep);

        static string ComputeCode(byte[] key, long timestep)
        {
            var counter = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counter, timestep);
            var hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24) |
                         (hash[offset + 1] << 16) |
                         (hash[offset + 2] << 8) |
                         hash[offset + 3];
            return (binary % 1_000_000).ToString("D6");
        }

        public static string Base32Encode(byte[] data)
        {
            var result = new StringBuilder((data.Length * 8 + 4) / 5);
            var buffer = 0;
            var bits = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    result.Append(Base32Chars[(buffer >> bits) & 0x1F]);
                }
            }
            if (bits > 0) result.Append(Base32Chars[(buffer << (5 - bits)) & 0x1F]);
            return result.ToString();
        }

        public static byte[] Base32Decode(string text)
        {
            var result = new List<byte>(text.Length * 5 / 8);
            var buffer = 0;
            var bits = 0;
            foreach (var c in text.TrimEnd('='))
            {
                if (c == ' ' || c == '-') continue;
                var value = Base32Chars.IndexOf(char.ToUpperInvariant(c));
                if (value < 0) throw new FormatException("Invalid Base32 character.");
                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    result.Add((byte)((buffer >> bits) & 0xFF));
                }
            }
            return [.. result];
        }
    }
}
