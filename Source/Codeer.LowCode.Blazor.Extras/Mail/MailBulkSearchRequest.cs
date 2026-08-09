using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// Wire format of POST /api/mail/bulk_search (recipients are resolved on the server
    /// from the search condition, so addresses never travel to the client).
    /// </summary>
    public class MailBulkSearchRequest
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
        public SearchCondition Condition { get; set; } = new();
        /// <summary>Field name of the target row module that holds the mail address.</summary>
        public string ToField { get; set; } = string.Empty;
        /// <summary>Boolean field name of the target row module. Rows with true are excluded (opt-out).</summary>
        public string ExcludeField { get; set; } = string.Empty;
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }
}
