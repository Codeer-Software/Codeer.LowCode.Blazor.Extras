namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// The part of a bulk send that is common to all recipients (IMailSender contract).
    /// The server builds this from the bulk requests; it does not travel on the wire itself.
    /// </summary>
    public class MailBulkTemplate
    {
        /// <summary>差出人アドレス (任意)。空 = 送信者設定の差出人。AllowedFromDomains の検証は単発送信と同じ。</summary>
        public string From { get; set; } = string.Empty;

        /// <summary>差出人表示名 (From 指定時のみ使われる)。</summary>
        public string FromDisplayName { get; set; } = string.Empty;

        /// <summary>Subject template. {Name} tokens are replaced per recipient.</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>Body template. {Name} tokens are replaced per recipient.</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        public List<MailAttachment> Attachments { get; set; } = new();
    }
}
