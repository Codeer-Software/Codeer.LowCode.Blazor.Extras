using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MailSender.Services
{
    /// <summary>プレビュー HTML に埋め込まれた 1 件のメール。</summary>
    public class MailPackageItem
    {
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>single のときは ", " 区切りで複数入ることがある (<see cref="ToAddresses"/> で分解)。</summary>
        public string To { get; set; } = string.Empty;
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        /// <summary>除外理由。null = 送信対象。"OptOut" / "NoAddress"。</summary>
        public string? Excluded { get; set; }

        [JsonIgnore]
        public List<string> ToAddresses
            => To.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).Where(e => e.Length > 0).ToList();

        [JsonIgnore]
        public bool IsExcluded => Excluded != null;

        [JsonIgnore]
        public bool IsSendTarget => Excluded == null && ToAddresses.Count > 0;

        [JsonIgnore]
        public string ExcludedText => Excluded switch
        {
            null => string.Empty,
            "OptOut" => "配信停止",
            "NoAddress" => "アドレスなし",
            _ => Excluded,
        };
    }

    public class MailPackageAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// 送信パッケージ = Web のプレビュー HTML の <c>&lt;script id="data" type="application/json"&gt;</c> の中身
    /// (Extras.Server の MailPreviewDocument が camelCase で出す)。
    /// </summary>
    public class MailPackage
    {
        public const int SupportedPackageVersion = 1;

        public string Kind { get; set; } = "single";
        public string Title { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;
        public string MailInfraName { get; set; } = string.Empty;
        public string ReplyTo { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string SubjectTemplate { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public List<string> Attachments { get; set; } = new();
        public int PackageVersion { get; set; }
        public List<MailPackageAttachment> AttachmentFiles { get; set; } = new();
        public int Total { get; set; }
        public int SendCount { get; set; }
        public List<MailPackageItem> Items { get; set; } = new();

        [JsonIgnore]
        public string SourceFile { get; set; } = string.Empty;

        static readonly Regex DataScript = new(
            @"<script\s+id=""data""\s+type=""application/json""\s*>(?<json>.*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>プレビュー HTML ファイルを読む。パッケージが無い / 未対応のバージョンは例外 (メッセージはそのまま表示できる)。</summary>
        public static MailPackage Load(string path)
        {
            var html = File.ReadAllText(path);
            var match = DataScript.Match(html);
            if (!match.Success)
                throw new InvalidDataException("このファイルには送信パッケージが含まれていません。Web アプリのメールの「プレビュー」でダウンロードした HTML を選んでください。");

            var package = JsonSerializer.Deserialize<MailPackage>(match.Groups["json"].Value, JsonOptions)
                ?? throw new InvalidDataException("送信パッケージを読み取れませんでした。");
            if (package.PackageVersion < 1)
                throw new InvalidDataException("このプレビュー HTML は古い形式で、送信パッケージを含んでいません。Web アプリを更新してプレビューを作り直してください。");
            if (package.PackageVersion > SupportedPackageVersion)
                throw new InvalidDataException($"送信パッケージの形式 (バージョン {package.PackageVersion}) がこのアプリより新しいため開けません。MailSender を更新してください。");
            package.SourceFile = path;
            return package;
        }
    }
}
