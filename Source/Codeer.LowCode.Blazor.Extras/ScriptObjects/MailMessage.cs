using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// 旧 (0.5.0) のメール API の添付 1 件。<see cref="MailMessage"/> と組で、当時のテンプレート (MailController / SmtpMailService) を
    /// そのまま動かすために残している。新規は MailField / BulkMailField (Codeer.LowCode.Blazor.Extras.Mail) を使う。
    /// </summary>
    public class MailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// 旧 (0.5.0) のメール API: スクリプトで組み立ててサーバー (/api/mail) に POST するメッセージ。
    /// アドレスは ';' 区切りで複数指定できる。<see cref="MailService"/> / Extras.Server の SmtpMailService と組で、
    /// 0.5.0 のテンプレートを変更なしで動かすために残している。新規は MailField / BulkMailField を使う。
    /// </summary>
    public class MailMessage
    {
        public List<string> To { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        public List<MailAttachment> Attachments { get; set; } = new();

        [ScriptName("AddTo")]
        public MailMessage AddTo(string address)
        {
            To.AddRange(Split(address));
            return this;
        }

        [ScriptName("AddCc")]
        public MailMessage AddCc(string address)
        {
            Cc.AddRange(Split(address));
            return this;
        }

        [ScriptName("AddBcc")]
        public MailMessage AddBcc(string address)
        {
            Bcc.AddRange(Split(address));
            return this;
        }

        [ScriptName("SetSubject")]
        public MailMessage SetSubject(string subject)
        {
            Subject = subject;
            return this;
        }

        [ScriptName("SetBody")]
        public MailMessage SetBody(string body)
        {
            Body = body;
            IsBodyHtml = false;
            return this;
        }

        [ScriptName("SetHtmlBody")]
        public MailMessage SetHtmlBody(string html)
        {
            Body = html;
            IsBodyHtml = true;
            return this;
        }

        [ScriptName("SetReplyTo")]
        public MailMessage SetReplyTo(string address)
        {
            ReplyTo = address;
            return this;
        }

        [ScriptName("AddAttachment")]
        public MailMessage AddAttachment(string fileName, Excel excel)
        {
            Attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(excel.GetBytes()) });
            return this;
        }

        [ScriptName("AddTextAttachment")]
        public MailMessage AddTextAttachment(string fileName, string text)
        {
            Attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) });
            return this;
        }

        /// <summary>新しい送信インフラ (<see cref="Codeer.LowCode.Blazor.Extras.Server"/> の IMailSender) に渡す形へ変換する。</summary>
        [ScriptHide]
        public Codeer.LowCode.Blazor.Extras.Mail.MailMessage ToMailMessage() => new()
        {
            To = To.ToList(),
            Cc = Cc.ToList(),
            Bcc = Bcc.ToList(),
            Subject = Subject,
            Body = Body,
            IsBodyHtml = IsBodyHtml,
            ReplyTo = ReplyTo,
            Attachments = Attachments.Select(e => new Codeer.LowCode.Blazor.Extras.Mail.MailAttachment { FileName = e.FileName, ContentBase64 = e.ContentBase64 }).ToList(),
        };

        static IEnumerable<string> Split(string address)
            => address.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e));
    }
}
