using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// Bulk mail built and sent from scripts. Subject/Body are templates whose {FieldName} tokens
    /// are resolved per recipient. Set exactly one recipient source (Rows / Searcher / AddRecipient):
    /// <code>
    /// var bulk = new BulkMail();
    /// bulk.Sender = "Campaign";
    /// bulk.Subject = "{Name} 様へのご案内";
    /// bulk.Body = "担当は {SalesName} です";
    /// bulk.ToField = "Email";
    /// bulk.ExcludeField = "OptOut";     //trueの行には送らない(配信停止)
    /// bulk.Rows = CustomerList.Rows;    //または bulk.Searcher = searcher(サーバー解決・大量向け)
    /// bulk.Source = this;
    /// var result = bulk.Send();
    /// </code>
    /// </summary>
    public class BulkMail
    {
        [ScriptHide, ScriptInject]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        [ScriptHide, ScriptInject]
        public IHttpService? Http { get; set; }

        /// <summary>Sender name configured in appsettings (Mail.Senders). Empty = the first sender.</summary>
        public string Sender { get; set; } = string.Empty;

        /// <summary>Subject template ({FieldName} tokens).</summary>
        public string Subject { get; set; } = string.Empty;

        /// <summary>Body template ({FieldName} tokens). For HTML bodies the resolved values are HTML-escaped.</summary>
        public string Body { get; set; } = string.Empty;

        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>Field name of the target row module that holds the mail address (Rows/Searcher sources).</summary>
        public string ToField { get; set; } = string.Empty;

        /// <summary>Boolean field name of the target row module. Rows with true are excluded (opt-out).</summary>
        public string ExcludeField { get; set; } = string.Empty;

        /// <summary>Record this send originates from. Recorded as SourceModule/SourceId in the send history.</summary>
        public Module? Source { get; set; }

        /// <summary>Target rows loaded in the script (list rows / search results). Resolved on the client.</summary>
        public List<Module>? Rows { get; set; }

        /// <summary>
        /// Target search condition. Recipients are resolved on the server (addresses never travel
        /// to the client), which also makes this the path for large sends. {RecordUrl} is available
        /// on this path (appsettings Mail.AppBaseUrl).
        /// </summary>
        public ModuleSearcher? Searcher { get; set; }

        readonly List<MailBulkRecipient> _recipients = new();
        readonly List<MailAttachment> _attachments = new();

        /// <summary>Adds a hand-built recipient (the third recipient source).</summary>
        [ScriptName("AddRecipient")]
        public MailRecipient AddRecipient(string to)
        {
            var recipient = new MailRecipient { To = to };
            _recipients.Add(recipient);
            return recipient;
        }

        [ScriptName("AddAttachment")]
        public BulkMail AddAttachment(string fileName, Excel excel)
        {
            _attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(excel.GetBytes()) });
            return this;
        }

        [ScriptName("AddTextAttachment")]
        public BulkMail AddTextAttachment(string fileName, string text)
        {
            _attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) });
            return this;
        }

        /// <summary>Returns the body resolved with the row's values (send preview).</summary>
        [ScriptName("Preview")]
        public string Preview(Module row)
            => MailTemplateEngine.Fill(Body, ResolveVariables(row));

        /// <summary>Returns the subject resolved with the row's values (send preview).</summary>
        [ScriptName("PreviewSubject")]
        public string PreviewSubject(Module row)
            => MailTemplateEngine.Fill(Subject, ResolveVariables(row));

        [ScriptName("Send")]
        public async Task<MailSendResult> SendAsync()
        {
            //宛先ソースはちょうど1つ(複数セットはユーザー責任=失敗として報告する)
            var sourceCount = (Rows != null ? 1 : 0) + (Searcher != null ? 1 : 0) + (_recipients.Any() ? 1 : 0);
            if (sourceCount == 0)
                return MailSendResult.Failure(string.Empty, "No recipient source is set (Rows / Searcher / AddRecipient).");
            if (sourceCount > 1)
                return MailSendResult.Failure(string.Empty, "Multiple recipient sources are set (Rows / Searcher / AddRecipient).");

            var sourceModule = Source?.Design.Name ?? string.Empty;
            var sourceId = Source?.GetIdText() ?? string.Empty;

            var result = Searcher != null
                ? await MailTransport.SendBulkSearchAsync(Http, new MailBulkSearchRequest
                {
                    SenderName = Sender,
                    Subject = Subject,
                    Body = Body,
                    IsBodyHtml = IsBodyHtml,
                    ReplyTo = ReplyTo,
                    Attachments = _attachments.ToList(),
                    Condition = Searcher.GetSearchCondition(),
                    ToField = ToField,
                    ExcludeField = ExcludeField,
                    SourceModule = sourceModule,
                    SourceId = sourceId,
                })
                : await MailTransport.SendBulkAsync(Http, new MailBulkRequest
                {
                    SenderName = Sender,
                    Subject = Subject,
                    Body = Body,
                    IsBodyHtml = IsBodyHtml,
                    ReplyTo = ReplyTo,
                    Attachments = _attachments.ToList(),
                    Recipients = Rows != null ? BuildRecipients(Rows) : _recipients,
                    SourceModule = sourceModule,
                    SourceId = sourceId,
                });
            await Mail.LogFailuresAsync(Services, result);
            return result;
        }

        List<MailBulkRecipient> BuildRecipients(List<Module> rows)
        {
            var names = MailRecipientBuilder.GetVariableNames(Subject, Body);
            return rows
                .Select(row => MailRecipientBuilder.TryBuild(row.Design, row.GetData(), ToField, ExcludeField, names))
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();
        }

        Dictionary<string, string> ResolveVariables(Module row)
        {
            var names = MailTemplateEngine.GetVariableNames(Subject)
                .Concat(MailTemplateEngine.GetVariableNames(Body))
                .Distinct();
            return MailVariableResolver.Resolve(row.Design, row.GetData(), names);
        }
    }
}
