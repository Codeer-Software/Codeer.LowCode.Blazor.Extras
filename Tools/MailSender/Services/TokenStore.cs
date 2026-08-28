using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MailSender.Services
{
    /// <summary>メールのプロバイダ種別 (今は Gmail だけ。Graph / SMTP を足すときはここに増やす)。</summary>
    public static class MailProviders
    {
        public const string Gmail = "Gmail";
    }

    /// <summary>
    /// 登録したアカウント 1 件 (差出人)。プロバイダごとに使う項目が違う:
    /// Gmail = RefreshToken + Email。
    /// </summary>
    public class StoredAccount
    {
        public string Provider { get; set; } = MailProviders.Gmail;
        public string Email { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }

        /// <summary>OAuth 系 (Gmail) のリフレッシュトークン。</summary>
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>登録済みアカウントの一覧と、今送信に使うアカウント。</summary>
    public class StoredAccounts
    {
        public List<StoredAccount> Accounts { get; set; } = new();
        public string SelectedEmail { get; set; } = string.Empty;

        /// <summary>送信に使うアカウント (選択が無ければ先頭)。</summary>
        public StoredAccount? Selected
            => Accounts.FirstOrDefault(e => e.Email == SelectedEmail) ?? Accounts.FirstOrDefault();

        /// <summary>同じプロバイダ・同じアドレスがあれば差し替え、無ければ追加して選択する。</summary>
        public void AddOrReplace(StoredAccount account)
        {
            Accounts.RemoveAll(e => e.Provider == account.Provider && e.Email == account.Email);
            Accounts.Add(account);
            Accounts.Sort((a, b) => string.Compare(a.Email, b.Email, StringComparison.OrdinalIgnoreCase));
            SelectedEmail = account.Email;
        }

        public void Remove(StoredAccount account)
        {
            Accounts.Remove(account);
            if (SelectedEmail == account.Email) SelectedEmail = Accounts.FirstOrDefault()?.Email ?? string.Empty;
        }
    }

    /// <summary>
    /// アカウントの保管 (%LOCALAPPDATA%\Codeer\MailSender\accounts.bin)。複数アカウントを切り替えて使える。
    /// DPAPI (CurrentUser) で暗号化するので、同じ Windows アカウントでしか復号できない。PC の外には出さない。
    /// </summary>
    public static class TokenStore
    {
        static string FilePath => Path.Combine(AppSettings.DataFolder, "accounts.bin");

        public static StoredAccounts Load()
        {
            var accounts = Read<StoredAccounts>(FilePath) ?? new();
            //Provider が無い古い行は Gmail
            foreach (var a in accounts.Accounts.Where(a => string.IsNullOrEmpty(a.Provider))) a.Provider = MailProviders.Gmail;
            return accounts;
        }

        static T? Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(plain));
            }
            catch
            {
                //別のアカウントで作られた / 壊れている → 未登録として扱う (発行し直す)
                return null;
            }
        }

        public static void Save(StoredAccounts accounts)
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            if (accounts.Accounts.Count == 0)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                return;
            }
            var plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(accounts));
            File.WriteAllBytes(FilePath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
        }
    }
}
