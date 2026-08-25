using System.Security.Cryptography;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// <see cref="Codeer.LowCode.Blazor.Extras.Designs.GmailTokenFieldDesign"/> の列を保存するときの暗号化。
    /// AES-GCM 256bit・毎回ランダムな nonce で、保存形式は "v1:" + Base64(nonce | ciphertext | tag)。
    /// 方式を公開しても安全性は鍵だけに依存する (鍵は appsettings の Gmail.TokenEncryptionKey)。
    /// リポジトリやデザインファイルに鍵を置かないこと。将来方式を変える場合は "v2:" を足して読み分ける。
    /// </summary>
    /// <remarks>
    /// パスワードのようにハッシュ (一方向) にはできない。送信時に本物のトークンが必要なため可逆でなければならず、
    /// だから鍵の管理が必要になる。守れる範囲は「DBのダンプ・バックアップ・レプリカ・SQL経由の漏えい」で、
    /// サーバー自体が奪われた場合 (鍵も一緒に読める) は守れない。
    /// </remarks>
    internal static class GmailTokenProtector
    {
        const string Version1Prefix = "v1:";
        const int NonceSize = 12;
        const int TagSize = 16;

        /// <summary>平文のトークンを暗号化する。鍵が未設定なら例外 (平文で保存しない)。</summary>
        public static string Protect(string plainText, string encryptionKey)
        {
            var key = CreateKey(encryptionKey);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plain = Encoding.UTF8.GetBytes(plainText);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plain, cipher, tag);
            }
            var stored = new byte[NonceSize + cipher.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, stored, 0, NonceSize);
            Buffer.BlockCopy(cipher, 0, stored, NonceSize, cipher.Length);
            Buffer.BlockCopy(tag, 0, stored, NonceSize + cipher.Length, TagSize);
            return Version1Prefix + Convert.ToBase64String(stored);
        }

        /// <summary>暗号化された値を復号する。形式違い・鍵違い・改変は例外。</summary>
        public static string Unprotect(string storedValue, string encryptionKey)
        {
            if (!IsProtected(storedValue))
                throw new InvalidOperationException("The stored token is not encrypted (missing the 'v1:' prefix). Register the token again.");

            var key = CreateKey(encryptionKey);
            var stored = Convert.FromBase64String(storedValue.Substring(Version1Prefix.Length));
            if (stored.Length < NonceSize + TagSize)
                throw new InvalidOperationException("The stored token is broken (too short).");

            var nonce = stored.AsSpan(0, NonceSize);
            var cipher = stored.AsSpan(NonceSize, stored.Length - NonceSize - TagSize);
            var tag = stored.AsSpan(stored.Length - TagSize, TagSize);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }
            return Encoding.UTF8.GetString(plain);
        }

        /// <summary>暗号化済みの形式かどうか。</summary>
        public static bool IsProtected(string? storedValue)
            => !string.IsNullOrEmpty(storedValue) && storedValue.StartsWith(Version1Prefix, StringComparison.Ordinal);

        //設定文字列は長さ自由 (パスフレーズでも Base64 でも可) なので SHA-256 で 256bit 鍵に畳む
        static byte[] CreateKey(string encryptionKey)
        {
            if (string.IsNullOrEmpty(encryptionKey))
                throw new InvalidOperationException("Gmail.TokenEncryptionKey is not configured. Set it before storing Gmail tokens.");
            return SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
        }
    }
}
