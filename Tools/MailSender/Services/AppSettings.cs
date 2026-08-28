using System.IO;
using System.Text.Json;

namespace MailSender.Services
{
    /// <summary>
    /// Gmail 用の設定。Google Cloud でダウンロードした OAuth クライアントの client_secret.json (installed 種別) から取り込む。
    /// デスクトップ種別の client_secret は Google の仕様上「秘密ではない」扱いだが、それでも他人に配る物ではない。
    /// </summary>
    public class GmailClientSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        public bool IsConfigured => !string.IsNullOrEmpty(ClientId);

        /// <summary>
        /// client_secret.json を読む。
        /// 形式: {"installed":{"client_id":"...","client_secret":"...",...}} (web 種別の {"web":{...}} も受ける)。
        /// </summary>
        public static GmailClientSettings FromClientSecretJson(string path)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            if (!root.TryGetProperty("installed", out var app) && !root.TryGetProperty("web", out app))
                throw new InvalidOperationException("OAuth クライアントの JSON ではありません (\"installed\" または \"web\" がありません)。\nGoogle Cloud の「認証情報」でクライアント ID の行の右端からダウンロードした JSON を選んでください。");
            var clientId = app.TryGetProperty("client_id", out var id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(clientId)) throw new InvalidOperationException("JSON に client_id がありません。");
            return new GmailClientSettings
            {
                ClientId = clientId,
                ClientSecret = app.TryGetProperty("client_secret", out var secret) ? secret.GetString() ?? string.Empty : string.Empty,
            };
        }
    }

    /// <summary>
    /// アプリ設定 (%LOCALAPPDATA%\Codeer\MailSender\settings.json)。
    /// プロバイダごとのセクションを持つ (今は Gmail だけ。Graph / SMTP を足すときは横に並べる)。
    /// </summary>
    public class AppSettings
    {
        public static string DataFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codeer", "MailSender");

        static string FilePath => Path.Combine(DataFolder, "settings.json");

        public GmailClientSettings Gmail { get; set; } = new();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch
            {
                //壊れていれば既定値 (設定画面で入れ直す)
            }
            return new();
        }

        public void Save()
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
