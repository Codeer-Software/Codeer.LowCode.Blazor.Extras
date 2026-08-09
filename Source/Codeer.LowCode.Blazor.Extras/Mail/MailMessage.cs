namespace Codeer.LowCode.Blazor.Extras.Mail
{
    public class MailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mail message data carried between the Mail script object, the mail endpoint and
    /// the IMailSender infrastructures.
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

        /// <summary>
        /// Additional message headers (e.g. X-CLB-Original-To set by the redirect-all safety net).
        /// Header names should start with "X-".
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();
    }
}
