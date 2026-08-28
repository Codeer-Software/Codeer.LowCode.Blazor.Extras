using System.IO;
using System.Text.Json;

namespace MailSender.Services
{
    /// <summary>
    /// Gmail 用の OAuth クライアント 1 つ分 (Google Cloud の「認証情報」で作ったもの)。
    /// デスクトップ種別 = 個人が自分名義で送るための exe 用。Web 種別 = Web アプリの共通送信者のトークンを発行する用。
    /// デスクトップ種別の client_secret は Google の仕様上「秘密ではない」扱いだが、それでも他人に配る物ではない。
    /// </summary>
    public class GmailClientSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>
        /// Web 種別 ("web") のクライアントか。Web 種別は Google に事前登録したリダイレクト URI と完全一致が必要なので、
        /// 同意フローで <see cref="RedirectUri"/> の固定ポートを使う (デスクトップ種別は任意ポート)。
        /// </summary>
        public bool IsWebClient { get; set; }

        /// <summary>Web 種別で使う固定リダイレクト URI。Google Cloud の「承認済みのリダイレクト URI」にこの値を登録する。</summary>
        public string RedirectUri { get; set; } = DefaultWebRedirectUri;

        public const string DefaultWebRedirectUri = "http://localhost:53682/";

        public bool IsConfigured => !string.IsNullOrEmpty(ClientId);

        public string DisplayName => IsWebClient ? "ウェブ アプリケーション" : "デスクトップ アプリ";

        /// <summary>
        /// Google の client_secret.json と同じ形の JSON を作る (Web アプリの Gmail.ClientSecret 用)。
        /// Google Cloud コンソールで JSON をダウンロードできないときの代替。
        /// </summary>
        public string ToClientSecretJson()
        {
            var app = new Dictionary<string, object>
            {
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["auth_uri"] = "https://accounts.google.com/o/oauth2/auth",
                ["token_uri"] = "https://oauth2.googleapis.com/token",
            };
            if (IsWebClient) app["redirect_uris"] = new[] { RedirectUri };
            var root = new Dictionary<string, object> { [IsWebClient ? "web" : "installed"] = app };
            return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// client_secret.json を読む。
        /// 形式: {"installed":{"client_id":"...","client_secret":"...",...}} (web 種別の {"web":{...}} も受ける)。
        /// </summary>
        public static GmailClientSettings FromClientSecretJson(string path)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            var isWeb = false;
            if (!root.TryGetProperty("installed", out var app))
            {
                if (!root.TryGetProperty("web", out app))
                    throw new InvalidOperationException("OAuth クライアントの JSON ではありません (\"installed\" または \"web\" がありません)。\nGoogle Cloud の「認証情報」でクライアント ID の行の右端からダウンロードした JSON を選んでください。");
                isWeb = true;
            }
            var clientId = app.TryGetProperty("client_id", out var id) ? id.GetString() : null;
            if (string.IsNullOrEmpty(clientId)) throw new InvalidOperationException("JSON に client_id がありません。");

            //Web 種別: JSON の redirect_uris に localhost のものがあればそれを使う (無ければ既定値。Google 側に登録が必要)
            var redirectUri = DefaultWebRedirectUri;
            if (isWeb && app.TryGetProperty("redirect_uris", out var uris) && uris.ValueKind == JsonValueKind.Array)
            {
                var local = uris.EnumerateArray().Select(e => e.GetString()).FirstOrDefault(u =>
                    u != null && (u.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)));
                if (local != null) redirectUri = local.EndsWith('/') ? local : local + "/";
            }
            return new GmailClientSettings
            {
                ClientId = clientId,
                ClientSecret = app.TryGetProperty("client_secret", out var secret) ? secret.GetString() ?? string.Empty : string.Empty,
                IsWebClient = isWeb,
                RedirectUri = redirectUri,
            };
        }
    }

    /// <summary>Gmail の設定。デスクトップ種別と Web 種別のクライアントを両方登録でき、発行時に選ぶ。</summary>
    public class GmailSettings
    {
        public GmailClientSettings Desktop { get; set; } = new() { IsWebClient = false };
        public GmailClientSettings Web { get; set; } = new() { IsWebClient = true };

        /// <summary>登録済みのクライアント (デスクトップ → Web の順)。</summary>
        public IEnumerable<GmailClientSettings> Configured
        {
            get
            {
                if (Desktop.IsConfigured) yield return Desktop;
                if (Web.IsConfigured) yield return Web;
            }
        }

        /// <summary>client_id からクライアントを探す (トークンは発行したクライアントでしかリフレッシュできない)。</summary>
        public GmailClientSettings? Find(string clientId)
            => Configured.FirstOrDefault(c => c.ClientId == clientId);

        /// <summary>種別の整合を取る (JSON を手で編集されても Desktop=installed / Web=web になるように)。</summary>
        public void Normalize()
        {
            Desktop.IsWebClient = false;
            Web.IsWebClient = true;
            if (string.IsNullOrEmpty(Web.RedirectUri)) Web.RedirectUri = GmailClientSettings.DefaultWebRedirectUri;
        }
    }

    /// <summary>
    /// アプリ設定 (%LOCALAPPDATA%\Codeer\MailSender\settings.json)。
    /// プロバイダごとのセクションを持つ (今は Gmail だけ。Graph / SMTP を足すときは横に並べる)。
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// データフォルダ。既定は %LOCALAPPDATA%\Codeer\MailSender。
        /// 環境変数 MAILSENDER_DATA_FOLDER で差し替えられる (検証・スクリーンショット撮影用に本番の設定/トークンと分けるため)。
        /// </summary>
        public static string DataFolder { get; } =
            Environment.GetEnvironmentVariable("MAILSENDER_DATA_FOLDER") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codeer", "MailSender");

        static string FilePath => Path.Combine(DataFolder, "settings.json");

        public GmailSettings Gmail { get; set; } = new();

        /// <summary>画面の拡大率 (Ctrl + マウスホイール)。</summary>
        public double Zoom { get; set; } = 1.0;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var text = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(text) ?? new();
                    MigrateSingleClient(text, settings);
                    settings.Gmail.Normalize();
                    return settings;
                }
            }
            catch
            {
                //壊れていれば既定値 (設定画面で入れ直す)
            }
            return new();
        }

        /// <summary>旧形式 (Gmail 直下に ClientId/ClientSecret/IsWebClient が 1 組) を Desktop / Web の該当スロットに移す。</summary>
        static void MigrateSingleClient(string text, AppSettings settings)
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                if (!json.RootElement.TryGetProperty("Gmail", out var gmail) || !gmail.TryGetProperty("ClientId", out _)) return;
                var single = JsonSerializer.Deserialize<GmailClientSettings>(gmail.GetRawText());
                if (single == null || !single.IsConfigured) return;
                if (single.IsWebClient) settings.Gmail.Web = single;
                else settings.Gmail.Desktop = single;
                settings.Save();
            }
            catch
            {
                //移行できなければ新形式の値のまま
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
