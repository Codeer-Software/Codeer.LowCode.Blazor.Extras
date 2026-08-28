using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MailSender.Services
{
    /// <summary>保存しているトークン (リフレッシュトークン + 同意したアカウント)。</summary>
    public class StoredToken
    {
        public string RefreshToken { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
    }

    /// <summary>
    /// トークンの保管 (%LOCALAPPDATA%\Codeer\MailSender\token.bin)。
    /// DPAPI (CurrentUser) で暗号化するので、同じ Windows アカウントでしか復号できない。PC の外には出さない。
    /// </summary>
    public static class TokenStore
    {
        static string FilePath => Path.Combine(AppSettings.DataFolder, "token.bin");

        public static StoredToken? Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<StoredToken>(Encoding.UTF8.GetString(plain));
            }
            catch
            {
                //別のアカウントで作られた / 壊れている → 未発行として扱う (発行し直す)
                return null;
            }
        }

        public static void Save(StoredToken token)
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
            File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }

        public static void Delete()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}
