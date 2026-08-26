using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>プレビュー HTML に埋め込むデータ (1 件のメール)。</summary>
    public class MailPreviewItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public List<MailTemplateSpan> SubjectSpans { get; set; } = new();
        public List<MailTemplateSpan> BodySpans { get; set; } = new();

        /// <summary>除外理由。null = 送信対象。"OptOut" / "NoAddress"。</summary>
        public string? Excluded { get; set; }
    }

    /// <summary>プレビュー HTML に埋め込むデータ全体。</summary>
    public class MailPreviewDocument
    {
        /// <summary>"bulk" (一斉: 左一覧・右本文) / "single" (単発: 1 枚)。</summary>
        public string Kind { get; set; } = "single";
        public string Title { get; set; } = string.Empty;
        public string GeneratedAt { get; set; } = string.Empty;
        public string MailInfraName { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string FromDisplayName { get; set; } = string.Empty;
        public bool IsFromCurrentUser { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string SubjectTemplate { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public List<string> Attachments { get; set; } = new();
        public int Total { get; set; }
        public int SendCount { get; set; }
        public int ExcludedByOptOut { get; set; }
        public int ExcludedByNoAddress { get; set; }

        /// <summary>送信インフラの一斉送信上限 (超えていれば送信時にエラーになる旨を表示)。</summary>
        public int? MaxBulkCount { get; set; }
        public string Warning { get; set; } = string.Empty;
        public List<MailPreviewItem> Items { get; set; } = new();

        static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>
    /// 送信せずに「送るとこうなる」を組み立てる (dry-run)。送信と同じ解決経路を使うので、プレビューと実送信は一致する。
    /// 一斉送信は宛先リストの検索条件から宛先を解決し、除外行 (配信停止 / アドレス空) も理由付きで含める。
    /// 出力は <see cref="MailPreviewHtml"/> で自己完結 HTML にする。
    /// </summary>
    public class MailPreviewBuilder
    {
        readonly MailDispatcher _dispatcher;
        readonly ModuleDataIO _moduleDataIO;
        readonly DesignData _designData;

        public MailPreviewBuilder(MailDispatcher dispatcher, ModuleDataIO moduleDataIO, DesignData designData)
        {
            _dispatcher = dispatcher;
            _moduleDataIO = moduleDataIO;
            _designData = designData;
        }

        /// <summary>一斉送信のプレビュー HTML。</summary>
        public async Task<string> BuildBulkHtmlAsync(MailBulkSearchRequest request)
            => MailPreviewHtml.Render(await BuildBulkAsync(request));

        /// <summary>単発送信のプレビュー HTML。</summary>
        public async Task<string> BuildSingleHtmlAsync(MailPreviewRequest request)
            => MailPreviewHtml.Render(await BuildSingleAsync(request));

        internal async Task<MailPreviewDocument> BuildBulkAsync(MailBulkSearchRequest request)
        {
            var set = await new MailBulkSearch(_dispatcher, _moduleDataIO, _designData).ResolveRecipientsAsync(request);
            var infraName = _dispatcher.ResolveBulkInfraName(request.MailInfraName);
            var doc = new MailPreviewDocument
            {
                Kind = "bulk",
                Title = string.IsNullOrEmpty(request.SourceModule) ? "Bulk mail" : $"{request.SourceModule} #{request.SourceId}",
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                MailInfraName = infraName,
                IsFromCurrentUser = request.IsFromCurrentUser,
                ReplyTo = request.ReplyTo,
                IsBodyHtml = request.IsBodyHtml,
                SubjectTemplate = request.Subject,
                BodyTemplate = request.Body,
                Attachments = request.Attachments.Select(e => e.FileName).ToList(),
                MaxBulkCount = _dispatcher.TryGetMaxBulkCount(infraName),
            };
            await FillFromAsync(doc, request.IsFromCurrentUser);

            foreach (var entry in set.Entries)
            {
                //送信時と同じ: HTML 本文は変数値をエスケープしてから差し込む
                var variables = request.IsBodyHtml
                    ? MailDispatcher.EncodeHtmlVariables(new MailBulkRecipient { Variables = entry.Variables }).Variables
                    : entry.Variables;
                var (subject, subjectSpans) = MailTemplateEngine.FillWithSpans(request.Subject, variables);
                var (body, bodySpans) = MailTemplateEngine.FillWithSpans(request.Body, variables);
                doc.Items.Add(new MailPreviewItem
                {
                    DisplayName = entry.DisplayName,
                    To = entry.To,
                    Subject = subject,
                    Body = body,
                    SubjectSpans = subjectSpans,
                    BodySpans = bodySpans,
                    Excluded = entry.Exclusion switch
                    {
                        MailRecipientExclusion.OptOut => "OptOut",
                        MailRecipientExclusion.NoAddress => "NoAddress",
                        _ => null,
                    },
                });
            }
            doc.Total = doc.Items.Count;
            doc.SendCount = doc.Items.Count(e => e.Excluded == null);
            doc.ExcludedByOptOut = doc.Items.Count(e => e.Excluded == "OptOut");
            doc.ExcludedByNoAddress = doc.Items.Count(e => e.Excluded == "NoAddress");
            if (doc.MaxBulkCount is int max && doc.SendCount > max)
                doc.Warning = $"送信対象 {doc.SendCount} 件が送信インフラ '{infraName}' の上限 {max} 件を超えています。このままでは送信時にエラーになります。";
            return doc;
        }

        internal async Task<MailPreviewDocument> BuildSingleAsync(MailPreviewRequest request)
        {
            var message = request.Message;
            var doc = new MailPreviewDocument
            {
                Kind = "single",
                Title = request.Title,
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                MailInfraName = _dispatcher.ResolveInfraName(request.MailInfraName),
                IsFromCurrentUser = request.IsFromCurrentUser,
                ReplyTo = message.ReplyTo,
                IsBodyHtml = message.IsBodyHtml,
                SubjectTemplate = request.SubjectTemplate,
                BodyTemplate = request.BodyTemplate,
                Attachments = message.Attachments.Select(e => e.FileName).ToList(),
                Total = 1,
                SendCount = 1,
            };
            await FillFromAsync(doc, request.IsFromCurrentUser);
            doc.Items.Add(new MailPreviewItem
            {
                To = string.Join(", ", message.To),
                Cc = message.Cc,
                Bcc = message.Bcc,
                Subject = message.Subject,
                Body = message.Body,
                SubjectSpans = request.SubjectSpans,
                BodySpans = request.BodySpans,
            });
            return doc;
        }

        //差出人: 「自分を差出人にする」なら操作ユーザー (送信時と同じ解決)。それ以外は送信インフラ設定の既定
        async Task FillFromAsync(MailPreviewDocument doc, bool isFromCurrentUser)
        {
            if (!isFromCurrentUser) return;
            var user = await _dispatcher.GetCurrentUserAsync();
            if (user == null)
            {
                doc.Warning = MailDispatcher.CurrentUserUnresolvedError;
                return;
            }
            doc.From = user.Email;
            doc.FromDisplayName = user.DisplayName;
        }
    }

    /// <summary>プレビューを自己完結 HTML (JS 内蔵・外部依存なし) にする。</summary>
    public static class MailPreviewHtml
    {
        const string ResourceName = "Codeer.LowCode.Blazor.Extras.Server.Mail.MailPreview.html";
        const string DataPlaceholder = "/*__PREVIEW_DATA__*/";

        public static string Render(MailPreviewDocument document)
        {
            using var stream = typeof(MailPreviewHtml).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Resource not found: {ResourceName}");
            using var reader = new StreamReader(stream);
            var template = reader.ReadToEnd();
            //script 要素の中に JSON を置くので "</" だけは閉じタグと誤認されないようにエスケープする
            var json = document.ToJson().Replace("</", "<\\/");
            return template.Replace(DataPlaceholder, json);
        }
    }
}
