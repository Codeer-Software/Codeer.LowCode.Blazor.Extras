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
    /// 「呼び名 → <see cref="IMailSender"/>」の対応表は**アプリのテンプレート側 (MailSenderTable)**
    /// が持つ (senderFactory)。製品はプロバイダ名も設定形式も知らないので、独自インフラも同じ対応表に足すだけ。
    /// </remarks>
    public class MailDispatcher
    {
        /// <summary>DebugRedirectAllTo 有効時に元の宛先を記録するヘッダ。</summary>
        internal const string OriginalToHeader = "X-CLB-Original-To";

        /// <summary>リダイレクトされた一斉送信の元の宛先件数を記録するヘッダ。</summary>
        internal const string OriginalTotalHeader = "X-CLB-Original-Total";

        //redirected bulk sends are clipped so that a staging environment never sends thousands of mails
        const int RedirectBulkClipCount = 10;

        readonly MailConfig _config;
        readonly Func<string, IMailSender?> _senderFactory;
        readonly MailHistoryWriter? _historyWriter;
        readonly Func<Task<MailCurrentUser?>>? _currentUserResolver;

        /// <param name="senderFactory">
        /// 呼び名 → 送信インフラの対応表 (テンプレートの MailSenderTable)。
        /// 知らない呼び名は null を返すとエラーになる。
        /// 空の呼び名はここには来ない (呼び名が空 = 設定漏れとして製品側がエラーにする)。
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
        internal async Task<MailCurrentUser?> GetCurrentUserAsync()
            => _currentUserResolver == null ? null : await _currentUserResolver();

        /// <summary>単発送信の呼び名: 明示名 → DefaultInfraName (どちらも空なら空)。</summary>
        internal string ResolveInfraName(string? mailInfraName)
            => string.IsNullOrEmpty(mailInfraName) ? _config.DefaultInfraName : mailInfraName;

        /// <summary>一斉送信の呼び名: 明示名 → DefaultBulkInfraName → DefaultInfraName。</summary>
        internal string ResolveBulkInfraName(string? mailInfraName)
            => string.IsNullOrEmpty(mailInfraName)
                ? (string.IsNullOrEmpty(_config.DefaultBulkInfraName) ? _config.DefaultInfraName : _config.DefaultBulkInfraName)
                : mailInfraName;

        /// <summary>
        /// 呼び名から送信インフラを引き当てる。呼び名が空 (指定なし・既定も未設定) と対応表が
        /// 知らない呼び名はどちらも例外 (設定ミスを黙って別のインフラで送らない)。
        /// 送信経路は例外ではなく失敗結果を返すので <see cref="FindSender"/> を使う。
        /// </summary>
        internal IMailSender CreateSender(string mailInfraName)
            => FindSender(mailInfraName, out var error) ?? throw new InvalidOperationException(error);

        /// <summary>呼び名が空 = フィールドの MailInfraName も appsettings の既定も未設定。</summary>
        const string NoInfraNameError =
            "No mail infra name is specified: the field's MailInfraName is empty and so is Mail.DefaultInfraName " +
            "(bulk: Mail.DefaultBulkInfraName) in appsettings. Set one of them to a name that the app's mail sender table (MailSenderTable) knows.";

        /// <summary>知らない呼び名 = 対応表 (MailSenderTable) にその名前が無い。</summary>
        static string UnknownInfraNameError(string mailInfraName)
            => $"Mail infra name '{mailInfraName}' is unknown: the app's mail sender table (MailSenderTable) has no entry for it. " +
                "Fix the name (field MailInfraName / Mail.DefaultInfraName / Mail.DefaultBulkInfraName) or add the infra to the table.";

        /// <summary>
        /// 呼び名から送信インフラを引き当てる。引き当てられないときは null + 理由
        /// (どちらの設定漏れなのかが分かる文言。送信経路はこれを失敗結果にして返す)。
        /// </summary>
        IMailSender? FindSender(string mailInfraName, out string error)
        {
            //空を対応表に渡さない = アプリの対応表が「空 = 何かのインフラ」と解釈して
            //設定漏れがそのインフラの別のエラー (SMTP未設定など) に化けるのを防ぐ
            if (string.IsNullOrEmpty(mailInfraName))
            {
                error = NoInfraNameError;
                return null;
            }
            var sender = _senderFactory(mailInfraName);
            error = sender == null ? UnknownInfraNameError(mailInfraName) : string.Empty;
            return sender;
        }

        //設定漏れも送信失敗と同じ扱いで返す (スクリプトの戻り値・トースト・履歴に理由が出る)
        async Task<MailSendResult> FailAsync(string mailInfraName, string subject, MailSendResult failure, MailHistorySource? source,
            IReadOnlyList<MailHistoryDetail>? details)
        {
            if (_historyWriter != null) await _historyWriter.WriteAsync(mailInfraName, subject, failure, source, details);
            return failure;
        }

        //送信明細 (履歴契約の Details が設定されているときだけ書かれる)。単発 = 宛先ごとに同じ文面
        static List<MailHistoryDetail> DetailsOf(MailMessage message, MailSendResult result)
        {
            var recipients = message.To.Count > 0 ? message.To : message.Cc.Count > 0 ? message.Cc : message.Bcc;
            var error = result.Failures.FirstOrDefault()?.Error ?? string.Empty;
            return recipients.Select(to => new MailHistoryDetail
            {
                To = to,
                Subject = message.Subject,
                Body = message.Body,
                IsSuccess = result.IsSuccess,
                Error = result.IsSuccess ? string.Empty : error,
            }).ToList();
        }

        //一斉 = 宛先ごとにテンプレートを解決した文面 (実際に送った内容)
        static List<MailHistoryDetail> DetailsOf(MailBulkTemplate template, List<MailBulkRecipient> recipients, MailSendResult result)
        {
            var failures = result.Failures.Where(e => !string.IsNullOrEmpty(e.To))
                .GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.First().Error);
            return recipients.Select(recipient =>
            {
                var message = SmtpMailSender.CreateResolvedMessage(template, recipient);
                var failed = failures.TryGetValue(recipient.To, out var error);
                return new MailHistoryDetail
                {
                    To = recipient.To,
                    Subject = message.Subject,
                    Body = message.Body,
                    IsSuccess = !failed,
                    Error = failed ? error! : string.Empty,
                };
            }).ToList();
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
                        CreateSource(request.SourceModule, request.SourceId), DetailsOf(message, failure));
                    return failure;
                }
                message.From = user.Email;
                message.FromDisplayName = user.DisplayName;
            }
            return await SendAsync(request.MailInfraName, message, CreateSource(request.SourceModule, request.SourceId));
        }

        internal const string CurrentUserUnresolvedError =
            "IsFromCurrentUser requires the current user's mail address (set the current user module in the design, put a MailSenderContractField on it, and make sure the user has an address).";

        internal static MailHistorySource? CreateSource(string sourceModule, string sourceId)
            => string.IsNullOrEmpty(sourceModule)
                ? null
                : new MailHistorySource { SourceModule = sourceModule, SourceId = sourceId };

        internal async Task<MailSendResult> SendAsync(string? mailInfraName, MailMessage message, MailHistorySource? source = null)
        {
            if (!message.To.Any() && !message.Cc.Any() && !message.Bcc.Any())
                return MailSendResult.Failure(string.Empty, "No recipients.");

            //履歴を取る設定なのに履歴モジュールが契約を満たしていないなら送らない (記録が静かに欠けるのを防ぐ)
            _historyWriter?.Validate();

            var name = ResolveInfraName(mailInfraName);
            var sender = FindSender(name, out var senderError);
            if (sender == null)
            {
                var failure = MailSendResult.Failure(string.Join(";", message.To), senderError);
                return await FailAsync(name, message.Subject, failure, source, DetailsOf(message, failure));
            }

            var sendMessage = string.IsNullOrEmpty(_config.DebugRedirectAllTo) ? message : Redirect(message);
            var result = await sender.SendAsync(sendMessage);
            if (_historyWriter != null) await _historyWriter.WriteAsync(name, message.Subject, result, source, DetailsOf(message, result));
            return result;
        }

        /// <summary>
        /// 1つのテンプレートを多数の宛先へ送る。MaxBulkCount 超過は例外 (黙って切り詰めない)。
        /// HTML テンプレートの変数値はここで一度だけ HTML エスケープする
        /// (ネイティブ一斉送信 (SendGrid) と逐次送信フォールバックの挙動を揃えるため)。
        /// </summary>
        internal async Task<MailSendResult> SendBulkAsync(string? mailInfraName, MailBulkTemplate template, List<MailBulkRecipient> recipients, MailHistorySource? source = null)
        {
            _historyWriter?.Validate();

            var name = ResolveBulkInfraName(mailInfraName);
            var sender = FindSender(name, out var senderError);
            if (sender == null)
            {
                var failure = new MailSendResult
                {
                    TotalCount = recipients.Count,
                    //宛先0件でも設定漏れは失敗として見せる (黙って成功にしない)
                    Failures = recipients.Count == 0
                        ? [new MailSendFailure { Error = senderError }]
                        : recipients.Select(e => new MailSendFailure { To = e.To, Error = senderError }).ToList(),
                };
                return await FailAsync(name, template.Subject, failure, source, DetailsOf(template, recipients, failure));
            }

            if (recipients.Count > sender.MaxBulkCount)
                throw new InvalidOperationException(
                    $"Bulk send of {recipients.Count} mails exceeds MaxBulkCount ({sender.MaxBulkCount}) of mail sender '{name}'.");

            if (template.IsBodyHtml) recipients = recipients.Select(EncodeHtmlVariables).ToList();

            var result = !string.IsNullOrEmpty(_config.DebugRedirectAllTo)
                ? await SendBulkRedirectedAsync(sender, template, recipients)
                : await sender.SendBulkAsync(template, recipients);
            if (_historyWriter != null) await _historyWriter.WriteAsync(name, template.Subject, result, source, DetailsOf(template, recipients, result));
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
