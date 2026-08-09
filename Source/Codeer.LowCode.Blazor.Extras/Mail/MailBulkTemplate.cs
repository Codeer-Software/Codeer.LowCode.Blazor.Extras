namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// The part of a bulk send that is common to all recipients (IMailSender contract).
    /// The server builds this from the bulk requests; it does not travel on the wire itself.
    /// </summary>
    public class MailBulkTemplate
    {
        /// <summary>Subject template. {Name} tokens are replaced per recipient.</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>Body template. {Name} tokens are replaced per recipient.</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        public List<MailAttachment> Attachments { get; set; } = new();
    }
}
