namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>Wire format of POST /api/mail/bulk (resolved recipient list built on the client).</summary>
    public class MailBulkRequest
    {
        public string SenderName { get; set; } = string.Empty;
        /// <summary>Subject template. {Name} tokens are replaced per recipient.</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>Body template. {Name} tokens are replaced per recipient.</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        /// <summary>Attachments shared by all recipients.</summary>
        public List<MailAttachment> Attachments { get; set; } = new();
        public List<MailBulkRecipient> Recipients { get; set; } = new();
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }
}
