namespace Codeer.LowCode.Blazor.Extras.Mail
{
    public class MailAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
    }

    /// <summary>
    /// メールメッセージ。Mail スクリプトオブジェクト・メールエンドポイント・
    /// 送信インフラ (IMailSender) の間で受け渡す。
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
        /// 追加のメッセージヘッダ (例: DebugRedirectAllTo の退避時に付く X-CLB-Original-To)。
        /// ヘッダ名は "X-" で始めること。
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new();
    }
}
