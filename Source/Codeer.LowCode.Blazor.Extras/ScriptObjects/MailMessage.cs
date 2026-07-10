using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class MailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mail message built from scripts and posted to the server (/api/mail).
    /// Addresses can contain multiple entries separated by ';'.
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

        static IEnumerable<string> Split(string address)
            => address.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).Where(e => !string.IsNullOrEmpty(e));
    }
}
