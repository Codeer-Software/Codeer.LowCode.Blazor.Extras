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
        /// <summary>
        /// 差出人アドレス (任意)。空 = 送信者設定 (Mail.Infras) の SenderMailAddress。
        /// 動的な From は送信者設定の AllowedFromDomains で許可されたドメインのみ (サーバーが検証する)。
        /// </summary>
        public string From { get; set; } = string.Empty;

        /// <summary>差出人表示名 (From 指定時のみ使われる)。</summary>
        public string FromDisplayName { get; set; } = string.Empty;

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
