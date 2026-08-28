using System.IO;
using System.Text.Json;

namespace MailSender.Services
{
    /// <summary>
    /// アプリ設定 (%LOCALAPPDATA%\Codeer\MailSender\settings.json)。
    /// デスクトップ種別の OAuth クライアントは client_id だけ (secret は無い。PKCE を使う)。
    /// </summary>
    public class AppSettings
    {
        public static string DataFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Codeer", "MailSender");

        static string FilePath => Path.Combine(DataFolder, "settings.json");

        public string ClientId { get; set; } = string.Empty;

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
