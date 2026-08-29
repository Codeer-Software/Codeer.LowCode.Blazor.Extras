using Codeer.Mail.Smtp;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MailSender.Services
{
    /// <summary>メールのプロバイダ種別。</summary>
    public static class MailProviders
    {
        public const string Gmail = "Gmail";
        public const string GraphApi = "GraphApi";
        public const string Smtp = "Smtp";

        public static string DisplayName(string provider) => provider switch
        {
            GraphApi => "Microsoft 365",
            Smtp => "SMTP",
            _ => "Gmail",
        };
    }

    /// <summary>
    /// 登録したアカウント 1 件 (差出人)。プロバイダごとに使う項目が違う:
    /// Gmail / Microsoft 365 = RefreshToken + ClientId (発行したクライアント) + Email。
    /// SMTP = <see cref="Smtp"/> (サーバー・認証・差出人)。Email はその差出人アドレスの写し。
    /// </summary>
    public class StoredAccount
    {
        public string Provider { get; set; } = MailProviders.Gmail;
        public string Email { get; set; } = string.Empty;

        /// <summary>差出人の表示名 (Microsoft 365 は本人の表示名、SMTP は登録した名前。Gmail は Google 側が付けるので空)。</summary>
        public string DisplayName { get; set; } = string.Empty;

        public DateTime IssuedAt { get; set; }

        /// <summary>OAuth 系 (Gmail / Microsoft 365) のリフレッシュトークン。Microsoft は更新のたびに差し替わることがある。</summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// このトークンを発行した OAuth クライアントの client_id。リフレッシュトークンは発行クライアントに紐づくので、
        /// 設定のクライアントと違うと送信 (リフレッシュ) できない。Web アプリ用に別クライアントで発行したものを見分けるためにも使う。
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>SMTP のサーバー・認証・差出人 (パスワード込み。ファイルごと DPAPI で暗号化される)。</summary>
        public SmtpAccountSettings? Smtp { get; set; }

        public bool IsGmail => Provider == MailProviders.Gmail;
        public bool IsGraphApi => Provider == MailProviders.GraphApi;
        public bool IsSmtp => Provider == MailProviders.Smtp;

        /// <summary>同一性のキーの第 3 要素 (OAuth = 発行クライアント / SMTP = ホスト)。</summary>
        public string KeyPart => IsSmtp ? Smtp?.Host ?? string.Empty : ClientId;
    }

    /// <summary>
    /// 登録済みアカウントの一覧と、今送信に使うアカウント。
    /// 同じアドレスでもプロバイダや発行クライアント (SMTP はホスト) が違えば別の行。
    /// </summary>
    public class StoredAccounts
    {
        public List<StoredAccount> Accounts { get; set; } = new();
        public string SelectedProvider { get; set; } = string.Empty;
        public string SelectedEmail { get; set; } = string.Empty;
        public string SelectedClientId { get; set; } = string.Empty;

        static bool SameKey(StoredAccount a, StoredAccount b)
            => a.Provider == b.Provider && string.Equals(a.Email, b.Email, StringComparison.OrdinalIgnoreCase) && a.KeyPart == b.KeyPart;

        /// <summary>送信に使うアカウント (選択が無ければ先頭)。</summary>
        public StoredAccount? Selected
            => Accounts.FirstOrDefault(e => e.Email == SelectedEmail && e.KeyPart == SelectedClientId && (string.IsNullOrEmpty(SelectedProvider) || e.Provider == SelectedProvider))
               ?? Accounts.FirstOrDefault(e => e.Email == SelectedEmail)
               ?? Accounts.FirstOrDefault();

        public void Select(StoredAccount? account)
        {
            SelectedProvider = account?.Provider ?? string.Empty;
            SelectedEmail = account?.Email ?? string.Empty;
            SelectedClientId = account?.KeyPart ?? string.Empty;
        }

        /// <summary>同じキーの行があれば差し替え (再発行 / 編集)、無ければ追加して選択する。差し替えたら true。</summary>
        public bool AddOrReplace(StoredAccount account)
        {
            var replaced = Accounts.RemoveAll(e => SameKey(e, account)) > 0;
            Accounts.Add(account);
            Accounts.Sort((a, b) =>
            {
                var c = string.Compare(a.Email, b.Email, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.Provider, b.Provider, StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.KeyPart, b.KeyPart, StringComparison.Ordinal);
            });
            Select(account);
            return replaced;
        }

        public void Remove(StoredAccount account)
        {
            Accounts.Remove(account);
            if (Selected == null || ReferenceEquals(Selected, account)) Select(Accounts.FirstOrDefault());
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
