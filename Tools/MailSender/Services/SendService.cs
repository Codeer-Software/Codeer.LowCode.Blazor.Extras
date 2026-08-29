using Codeer.Mail;
using Codeer.Mail.Gmail;
using Codeer.Mail.Graph;
using Codeer.Mail.Smtp;
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
    /// アカウント 1 件で送る手段 (プロバイダごとの実装)。<see cref="BeginAsync"/> → <see cref="SendAsync"/> × n → Dispose。
    /// 残りを送っても無駄な失敗は <see cref="MailSendAbortException"/> で送信ループに打ち切らせる。
    /// </summary>
    public abstract class AccountSender : IAsyncDisposable
    {
        public virtual Task BeginAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public abstract Task SendAsync(MailMessage message, CancellationToken cancellationToken);
        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// アカウントに対応する送信手段を作る。<paramref name="onAccountChanged"/> は保存すべき変更 (Microsoft のリフレッシュトークン差し替え) の通知。
        /// 設定不足 (発行クライアントが無い等) は null。
        /// </summary>
        public static AccountSender? Create(AppSettings settings, StoredAccount account, Action onAccountChanged)
        {
            if (account.IsSmtp) return account.Smtp == null ? null : new SmtpAccountSender(account.Smtp);
            if (account.IsGraphApi) return settings.GraphApi.IsConfigured ? new GraphAccountSender(settings.GraphApi, account, onAccountChanged) : null;
            var client = ResolveGmailClient(settings, account);
            return client == null ? null : new GmailAccountSender(client, account.RefreshToken);
        }

        /// <summary>
        /// Gmail アカウントのトークンをリフレッシュできるクライアント (発行したもの)。設定に無ければ null = 送れない。
        /// 古い行 (ClientId 無し) はデスクトップ扱い。
        /// </summary>
        public static GmailClientSettings? ResolveGmailClient(AppSettings settings, StoredAccount account)
            => string.IsNullOrEmpty(account.ClientId) ? (settings.Gmail.Desktop.IsConfigured ? settings.Gmail.Desktop : null)
             : settings.Gmail.Find(account.ClientId);
    }

    /// <summary>Gmail: 本人のリフレッシュトークン → アクセストークン (期限切れで更新) → Gmail API。レート制御 / 再試行は GmailApiClient。</summary>
    public class GmailAccountSender : AccountSender
    {
        readonly GmailClientSettings _client;
        readonly string _refreshToken;
        readonly GmailApiClient _api = new();
        readonly GmailOAuth _oauth = new();
        string? _accessToken;
        DateTime _accessTokenExpiresAtUtc;

        public GmailAccountSender(GmailClientSettings client, string refreshToken)
        {
            _client = client;
            _refreshToken = refreshToken;
        }

        public override async Task SendAsync(MailMessage message, CancellationToken cancellationToken)
        {
            await _api.WaitForNextSendAsync();
            await _api.SendAsync(GetAccessTokenAsync, message);
        }

        async Task<string> GetAccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresAtUtc) return _accessToken;
            var response = await _oauth.RefreshAsync(_client.ClientId, _client.ClientSecret, _refreshToken);
            _accessToken = response.AccessToken;
            _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, response.ExpiresInSeconds - 60));
            return _accessToken;
        }
    }

    /// <summary>Microsoft 365: 本人のリフレッシュトークン → アクセストークン → Graph /me/sendMail。リフレッシュトークンが差し替わったら保存を依頼する。</summary>
    public class GraphAccountSender : AccountSender
    {
        readonly GraphSettings _settings;
        readonly StoredAccount _account;
        readonly Action _onAccountChanged;
        readonly GraphApiClient _api = new();
        readonly GraphOAuth _oauth = new();
        string? _accessToken;
        DateTime _accessTokenExpiresAtUtc;

        public GraphAccountSender(GraphSettings settings, StoredAccount account, Action onAccountChanged)
        {
            _settings = settings;
            _account = account;
            _onAccountChanged = onAccountChanged;
        }

        public override async Task SendAsync(MailMessage message, CancellationToken cancellationToken)
        {
            await _api.WaitForNextSendAsync();
            await _api.SendAsync(GetAccessTokenAsync, message);
        }

        async Task<string> GetAccessTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _accessTokenExpiresAtUtc) return _accessToken;
            GraphTokenResponse response;
            try
            {
                response = await _oauth.RefreshAsync(_settings.EffectiveTenantId, _settings.ClientId, _account.RefreshToken);
            }
            catch (Exception ex)
            {
                //トークン失効 (90 日未使用・パスワード変更・管理者の取り消し) → 残りも送れない。「再発行」で取り直す
                throw new MailSendAbortException($"Microsoft 365 のトークンを更新できませんでした。「再発行」でサインインし直してください。{ex.Message}", ex);
            }
            _accessToken = response.AccessToken;
            _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, response.ExpiresInSeconds - 60));
            if (!string.IsNullOrEmpty(response.RefreshToken) && response.RefreshToken != _account.RefreshToken)
            {
                _account.RefreshToken = response.RefreshToken;
                _onAccountChanged();
            }
            return _accessToken;
        }
    }

    /// <summary>SMTP: 1 接続を開いたまま逐次送る。</summary>
    public class SmtpAccountSender : AccountSender
    {
        readonly SmtpSender _smtp;

        public SmtpAccountSender(SmtpAccountSettings settings)
        {
            _smtp = new SmtpSender(settings);
        }

        public override Task BeginAsync(CancellationToken cancellationToken) => _smtp.ConnectAsync(cancellationToken);
        public override Task SendAsync(MailMessage message, CancellationToken cancellationToken) => _smtp.SendAsync(message, cancellationToken);
        public override ValueTask DisposeAsync() => _smtp.DisposeAsync();
    }

    /// <summary>
    /// パッケージの送信ループ (プロバイダ共通)。1 通ずつ <see cref="AccountSender"/> で送り、
    /// <see cref="MailSendAbortException"/> (1 日の上限・接続不能・認証失敗) が出たら残りを失敗にして打ち切る。結果はローカルログにも残す。
    /// </summary>
    public class SendService
    {
        readonly AccountSender _sender;

        public SendService(AccountSender sender)
        {
            _sender = sender;
        }

        /// <summary>指定された項目 (画面でチェックされたもの) を順に送る。<paramref name="progress"/> は 1 件ごとに結果を通知する。</summary>
        public async Task<List<SendItemResult>> SendAsync(MailPackage package, IEnumerable<MailPackageItem> items, IProgress<SendItemResult> progress, CancellationToken cancellationToken)
        {
            var targets = items.Select(e => new SendItemResult(e)).ToList();
            var attachments = package.AttachmentFiles.Select(e => new MailAttachment { FileName = e.FileName, ContentBase64 = e.ContentBase64 }).ToList();

            await using (_sender)
            {
                var begun = false;
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
                        if (!begun)
                        {
                            await _sender.BeginAsync(cancellationToken);
                            begun = true;
                        }
                        await _sender.SendAsync(CreateMessage(package, target.Item, attachments), cancellationToken);
                        target.IsSuccess = true;
                    }
                    catch (MailSendAbortException ex)
                    {
                        //残りは送れないので打ち切る
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
            }
            return targets;
        }

        //From は空 = アカウント側で決まる (Gmail / Graph = トークンの本人、SMTP = 登録した差出人)
        static MailMessage CreateMessage(MailPackage package, MailPackageItem item, List<MailAttachment> attachments) => new()
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
