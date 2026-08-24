using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// プロバイダ非依存の送信レイヤ。名前付きインフラの解決・DebugRedirectAllTo の誤送信防止・
    /// 一斉送信の件数上限を適用してから <see cref="IMailSender"/> へ委譲する。
    /// 独自インフラはコンストラクタ引数 customSenderFactory で差し込める。
    /// </summary>
    public class MailDispatcher
    {
        /// <summary>DebugRedirectAllTo 有効時に元の宛先を記録するヘッダ。</summary>
        public const string OriginalToHeader = "X-CLB-Original-To";

        /// <summary>リダイレクトされた一斉送信の元の宛先件数を記録するヘッダ。</summary>
        public const string OriginalTotalHeader = "X-CLB-Original-Total";

        //redirected bulk sends are clipped so that a staging environment never sends thousands of mails
        internal const int RedirectBulkClipCount = 10;

        readonly MailConfig _config;
        readonly Func<MailInfraSettings, IMailSender?>? _customSenderFactory;
        readonly MailHistoryWriter? _historyWriter;
        readonly Func<Task<MailCurrentUser?>>? _currentUserResolver;

        public MailDispatcher(MailConfig config, Func<MailInfraSettings, IMailSender?>? customSenderFactory = null,
            MailHistoryWriter? historyWriter = null, Func<Task<MailCurrentUser?>>? currentUserResolver = null)
        {
            _config = config;
            _customSenderFactory = customSenderFactory;
            _historyWriter = historyWriter;
            _currentUserResolver = currentUserResolver;
        }

        /// <summary>
        /// 「自分を差出人にする」(IsFromCurrentUser) の操作ユーザー情報。
        /// 未結線・未設定・解決不能は null (呼び出し側が失敗にする)。
        /// </summary>
        public async Task<MailCurrentUser?> GetCurrentUserAsync()
            => _currentUserResolver == null ? null : await _currentUserResolver();

        /// <summary>単発送信のインフラ解決: 明示名 → DefaultInfraName → 先頭。</summary>
        public MailInfraSettings ResolveInfraSettings(string? mailInfraName)
            => ResolveCore(mailInfraName, _config.DefaultInfraName);

        /// <summary>一斉送信のインフラ解決: 明示名 → DefaultBulkInfraName → DefaultInfraName → 先頭。</summary>
        public MailInfraSettings ResolveBulkInfraSettings(string? mailInfraName)
            => ResolveCore(mailInfraName, string.IsNullOrEmpty(_config.DefaultBulkInfraName)
                ? _config.DefaultInfraName : _config.DefaultBulkInfraName);

        //設定された既定名がどれにも一致しない場合は明示名と同じく例外にする (設定ミスを黙って先頭に落とさない)
        MailInfraSettings ResolveCore(string? mailInfraName, string defaultName)
        {
            if (!_config.Infras.Any()) throw new InvalidOperationException("No mail senders are configured (Mail.Infras).");
            var name = string.IsNullOrEmpty(mailInfraName) ? defaultName : mailInfraName;
            if (string.IsNullOrEmpty(name)) return _config.Infras[0];
            return _config.Infras.FirstOrDefault(e => e.Name == name)
                ?? throw new InvalidOperationException($"Mail sender '{name}' is not configured.");
        }

        public IMailSender CreateSender(MailInfraSettings settings)
        {
            var custom = _customSenderFactory?.Invoke(settings);
            if (custom != null) return custom;
            return settings.Type switch
            {
                MailInfraTypes.GraphApi => new GraphApiMailSender(settings),
                MailInfraTypes.SendGrid => new SendGridMailSender(settings),
                MailInfraTypes.GmailApi => new GmailApiMailSender(settings),
                //Type 空 = 旧形式の設定は SMTP
                MailInfraTypes.Smtp or "" => new SmtpMailSender(settings),
                _ => throw new InvalidOperationException($"Unknown mail sender type '{settings.Type}'."),
            };
        }

        /// <summary>
        /// 単発送信のワイヤリクエスト (POST /api/mail) をそのまま送る。Controller を薄く保つための入口。
        /// 差出人はクライアントの値を信用せず、IsFromCurrentUser (自分を差出人にする) のときだけ
        /// サーバーが解決した操作ユーザーのアドレスにする (なりすましの構造的排除)。
        /// </summary>
        public async Task<MailSendResult> SendAsync(MailSendRequest request)
        {
            var message = request.Message;
            message.From = string.Empty;
            message.FromDisplayName = string.Empty;
            if (request.IsFromCurrentUser)
            {
                var user = await GetCurrentUserAsync();
                if (user == null)
                {
                    var failure = MailSendResult.Failure(string.Join(";", message.To), CurrentUserUnresolvedError);
                    if (_historyWriter != null) await _historyWriter.WriteAsync(request.MailInfraName, message.Subject, failure,
                        CreateSource(request.SourceModule, request.SourceId));
                    return failure;
                }
                message.From = user.Email;
                message.FromDisplayName = user.DisplayName;
            }
            return await SendAsync(request.MailInfraName, message, CreateSource(request.SourceModule, request.SourceId));
        }

        internal const string CurrentUserUnresolvedError =
            "IsFromCurrentUser requires the current user's mail address (configure Mail.UserModuleName / UserEmailFieldName and make sure the user has an address).";

        internal static MailHistorySource? CreateSource(string sourceModule, string sourceId)
            => string.IsNullOrEmpty(sourceModule)
                ? null
                : new MailHistorySource { SourceModule = sourceModule, SourceId = sourceId };

        public async Task<MailSendResult> SendAsync(string? mailInfraName, MailMessage message, MailHistorySource? source = null)
        {
            if (!message.To.Any() && !message.Cc.Any() && !message.Bcc.Any())
                return MailSendResult.Failure(string.Empty, "No recipients.");

            var settings = ResolveInfraSettings(mailInfraName);
            var sender = CreateSender(settings);
            var sendMessage = string.IsNullOrEmpty(_config.DebugRedirectAllTo) ? message : Redirect(message);
            var result = await sender.SendAsync(sendMessage);
            if (_historyWriter != null) await _historyWriter.WriteAsync(settings.Name, message.Subject, result, source);
            return result;
        }

        /// <summary>
        /// 1つのテンプレートを多数の宛先へ送る。MaxBulkCount 超過は例外 (黙って切り詰めない)。
        /// HTML テンプレートの変数値はここで一度だけ HTML エスケープする
        /// (ネイティブ一斉送信 (SendGrid) と逐次送信フォールバックの挙動を揃えるため)。
        /// </summary>
        public async Task<MailSendResult> SendBulkAsync(string? mailInfraName, MailBulkTemplate template, List<MailBulkRecipient> recipients, MailHistorySource? source = null)
        {
            var settings = ResolveBulkInfraSettings(mailInfraName);
            if (recipients.Count > settings.MaxBulkCount)
                throw new InvalidOperationException(
                    $"Bulk send of {recipients.Count} mails exceeds MaxBulkCount ({settings.MaxBulkCount}) of mail sender '{settings.Name}'.");

            if (template.IsBodyHtml) recipients = recipients.Select(EncodeHtmlVariables).ToList();

            var sender = CreateSender(settings);
            var result = !string.IsNullOrEmpty(_config.DebugRedirectAllTo)
                ? await SendBulkRedirectedAsync(sender, template, recipients)
                : await sender.SendBulkAsync(template, recipients);
            if (_historyWriter != null) await _historyWriter.WriteAsync(settings.Name, template.Subject, result, source);
            return result;
        }

        //インジェクション対策: 変数値にはユーザー入力が入りうる
        static MailBulkRecipient EncodeHtmlVariables(MailBulkRecipient recipient)
            => new()
            {
                To = recipient.To,
                Cc = recipient.Cc,
                Bcc = recipient.Bcc,
                Variables = recipient.Variables.ToDictionary(e => e.Key, e => WebUtility.HtmlEncode(e.Value)),
            };

        MailMessage Redirect(MailMessage src)
        {
            var originalTo = $"to: {string.Join(";", src.To)} cc: {string.Join(";", src.Cc)} bcc: {string.Join(";", src.Bcc)}";
            var redirected = new MailMessage
            {
                From = src.From,
                FromDisplayName = src.FromDisplayName,
                To = { _config.DebugRedirectAllTo },
                Subject = src.Subject,
                Body = src.Body,
                IsBodyHtml = src.IsBodyHtml,
                ReplyTo = src.ReplyTo,
                Attachments = src.Attachments,
                Headers = new Dictionary<string, string>(src.Headers),
            };
            redirected.Headers[OriginalToHeader] = originalTo;
            return redirected;
        }

        async Task<MailSendResult> SendBulkRedirectedAsync(IMailSender sender, MailBulkTemplate template, List<MailBulkRecipient> recipients)
        {
            var result = new MailSendResult { TotalCount = recipients.Count, SuccessCount = recipients.Count };
            foreach (var recipient in recipients.Take(RedirectBulkClipCount))
            {
                var message = SmtpMailSender.CreateResolvedMessage(template, recipient);
                message = Redirect(message);
                message.Headers[OriginalTotalHeader] = recipients.Count.ToString();
                var sendResult = await sender.SendAsync(message);
                if (!sendResult.IsSuccess)
                {
                    result.SuccessCount--;
                    result.Failures.Add(new MailSendFailure { To = recipient.To, Error = sendResult.Failures[0].Error });
                }
            }
            return result;
        }
    }
}
