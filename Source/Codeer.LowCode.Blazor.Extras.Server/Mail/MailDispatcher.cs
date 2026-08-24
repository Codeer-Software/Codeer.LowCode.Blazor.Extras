using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// プロバイダ非依存の送信レイヤ。呼び名から送信インフラを引き当て、DebugRedirectAllTo の誤送信防止・
    /// 一斉送信の件数上限を適用してから <see cref="IMailSender"/> へ委譲する。
    /// </summary>
    /// <remarks>
    /// 「呼び名 → <see cref="IMailSender"/>」の対応表は**アプリのテンプレート側 (MailController.CreateSender)**
    /// が持つ (senderFactory)。製品はプロバイダ名も設定形式も知らないので、独自インフラも同じ対応表に足すだけ。
    /// </remarks>
    public class MailDispatcher
    {
        /// <summary>DebugRedirectAllTo 有効時に元の宛先を記録するヘッダ。</summary>
        public const string OriginalToHeader = "X-CLB-Original-To";

        /// <summary>リダイレクトされた一斉送信の元の宛先件数を記録するヘッダ。</summary>
        public const string OriginalTotalHeader = "X-CLB-Original-Total";

        //redirected bulk sends are clipped so that a staging environment never sends thousands of mails
        internal const int RedirectBulkClipCount = 10;

        readonly MailConfig _config;
        readonly Func<string, IMailSender?> _senderFactory;
        readonly MailHistoryWriter? _historyWriter;
        readonly Func<Task<MailCurrentUser?>>? _currentUserResolver;

        /// <param name="senderFactory">
        /// 呼び名 → 送信インフラの対応表 (テンプレートの MailController.CreateSender)。
        /// 呼び名が空のときは既定を返すこと。知らない呼び名は null を返すとエラーになる。
        /// </param>
        public MailDispatcher(MailConfig config, Func<string, IMailSender?> senderFactory,
            MailHistoryWriter? historyWriter = null, Func<Task<MailCurrentUser?>>? currentUserResolver = null)
        {
            _config = config;
            _senderFactory = senderFactory;
            _historyWriter = historyWriter;
            _currentUserResolver = currentUserResolver;
        }

        /// <summary>
        /// 「自分を差出人にする」(IsFromCurrentUser) の操作ユーザー情報。
        /// 未結線・未設定・解決不能は null (呼び出し側が失敗にする)。
        /// </summary>
        public async Task<MailCurrentUser?> GetCurrentUserAsync()
            => _currentUserResolver == null ? null : await _currentUserResolver();

        /// <summary>単発送信の呼び名: 明示名 → DefaultInfraName (どちらも空なら空)。</summary>
        public string ResolveInfraName(string? mailInfraName)
            => string.IsNullOrEmpty(mailInfraName) ? _config.DefaultInfraName : mailInfraName;

        /// <summary>一斉送信の呼び名: 明示名 → DefaultBulkInfraName → DefaultInfraName。</summary>
        public string ResolveBulkInfraName(string? mailInfraName)
            => string.IsNullOrEmpty(mailInfraName)
                ? (string.IsNullOrEmpty(_config.DefaultBulkInfraName) ? _config.DefaultInfraName : _config.DefaultBulkInfraName)
                : mailInfraName;

        /// <summary>
        /// 呼び名から送信インフラを引き当てる。対応表が知らない呼び名は例外
        /// (設定ミスを黙って別のインフラで送らない)。
        /// </summary>
        public IMailSender CreateSender(string mailInfraName)
            => _senderFactory(mailInfraName)
                ?? throw new InvalidOperationException(string.IsNullOrEmpty(mailInfraName)
                    ? "No mail sender is configured (set Mail.DefaultInfraName, or return a default from MailController.CreateSender)."
                    : $"Mail sender '{mailInfraName}' is not configured.");

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

            var name = ResolveInfraName(mailInfraName);
            var sender = CreateSender(name);
            var sendMessage = string.IsNullOrEmpty(_config.DebugRedirectAllTo) ? message : Redirect(message);
            var result = await sender.SendAsync(sendMessage);
            if (_historyWriter != null) await _historyWriter.WriteAsync(name, message.Subject, result, source);
            return result;
        }

        /// <summary>
        /// 1つのテンプレートを多数の宛先へ送る。MaxBulkCount 超過は例外 (黙って切り詰めない)。
        /// HTML テンプレートの変数値はここで一度だけ HTML エスケープする
        /// (ネイティブ一斉送信 (SendGrid) と逐次送信フォールバックの挙動を揃えるため)。
        /// </summary>
        public async Task<MailSendResult> SendBulkAsync(string? mailInfraName, MailBulkTemplate template, List<MailBulkRecipient> recipients, MailHistorySource? source = null)
        {
            var name = ResolveBulkInfraName(mailInfraName);
            var sender = CreateSender(name);
            if (recipients.Count > sender.MaxBulkCount)
                throw new InvalidOperationException(
                    $"Bulk send of {recipients.Count} mails exceeds MaxBulkCount ({sender.MaxBulkCount}) of mail sender '{name}'.");

            if (template.IsBodyHtml) recipients = recipients.Select(EncodeHtmlVariables).ToList();

            var result = !string.IsNullOrEmpty(_config.DebugRedirectAllTo)
                ? await SendBulkRedirectedAsync(sender, template, recipients)
                : await sender.SendBulkAsync(template, recipients);
            if (_historyWriter != null) await _historyWriter.WriteAsync(name, template.Subject, result, source);
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
