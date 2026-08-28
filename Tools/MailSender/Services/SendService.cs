using Codeer.Mail.Gmail;
using System.IO;

namespace MailSender.Services
{
    /// <summary>1 件の送信結果。</summary>
    public class SendItemResult
    {
        public MailPackageItem Item { get; }
        public bool? IsSuccess { get; set; }
        public string Error { get; set; } = string.Empty;

        public SendItemResult(MailPackageItem item) { Item = item; }
    }

    /// <summary>
    /// パッケージの送信ループ。本人のリフレッシュトークン → アクセストークン (期限切れで更新) → 1 通ずつ Gmail API。
    /// レート制御 / 再試行は GmailApiClient、日次上限に達したら残りを失敗にして打ち切る。結果はローカルログにも残す。
    /// </summary>
    public class SendService
    {
        readonly GmailClientSettings _client;
        readonly string _refreshToken;
        readonly GmailApiClient _api = new();
        readonly GmailOAuth _oauth = new();
        string? _accessToken;
        DateTime _accessTokenExpiresAtUtc;

        public SendService(GmailClientSettings client, string refreshToken)
        {
            _client = client;
            _refreshToken = refreshToken;
        }

        /// <summary>指定された項目 (画面でチェックされたもの) を順に送る。<paramref name="progress"/> は 1 件ごとに結果を通知する。</summary>
        public async Task<List<SendItemResult>> SendAsync(MailPackage package, IEnumerable<MailPackageItem> items, IProgress<SendItemResult> progress, CancellationToken cancellationToken)
        {
            var targets = items.Select(e => new SendItemResult(e)).ToList();
            var attachments = package.AttachmentFiles.Select(e => new GmailAttachment { FileName = e.FileName, ContentBase64 = e.ContentBase64 }).ToList();

            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (cancellationToken.IsCancellationRequested)
                {
                    target.IsSuccess = false;
                    target.Error = "キャンセルされました";
                    Log(package, target);
                    progress.Report(target);
                    continue;
                }

                try
                {
                    await _api.WaitForNextSendAsync();
                    await _api.SendAsync(GetAccessTokenAsync, CreateMessage(package, target.Item, attachments));
                    target.IsSuccess = true;
                }
                catch (GmailDailyQuotaExceededException ex)
                {
                    //1 日の送信上限。残りは送れないので打ち切る
                    for (var j = i; j < targets.Count; j++)
                    {
                        targets[j].IsSuccess = false;
                        targets[j].Error = ex.Message;
                        Log(package, targets[j]);
                        progress.Report(targets[j]);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    target.IsSuccess = false;
                    target.Error = ex.Message;
                }
                Log(package, target);
                progress.Report(target);
            }
            return targets;
        }

        //From は空 = Gmail がトークンのアカウントのアドレスで補う (本人名義)
        static GmailMessage CreateMessage(MailPackage package, MailPackageItem item, List<GmailAttachment> attachments) => new()
        {
            To = item.ToAddresses,
            Cc = item.Cc.ToList(),
            Bcc = item.Bcc.ToList(),
            ReplyTo = package.ReplyTo,
            Subject = item.Subject,
            Body = item.Body,
            IsBodyHtml = package.IsBodyHtml,
            Attachments = attachments,
        };

        async Task<string> GetAccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresAtUtc) return _accessToken;
            var response = await _oauth.RefreshAsync(_client.ClientId, _client.ClientSecret, _refreshToken);
            _accessToken = response.AccessToken;
            _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, response.ExpiresInSeconds - 60));
            return _accessToken;
        }

        public static string LogFolder => Path.Combine(AppSettings.DataFolder, "logs");

        static void Log(MailPackage package, SendItemResult result)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{result.Item.To}\t{result.Item.Subject}\t{(result.IsSuccess == true ? "OK" : "NG")}\t{result.Error}\t{Path.GetFileName(package.SourceFile)}";
                File.AppendAllText(Path.Combine(LogFolder, $"{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
            catch
            {
                //ログが書けなくても送信は続ける
            }
        }
    }
}
