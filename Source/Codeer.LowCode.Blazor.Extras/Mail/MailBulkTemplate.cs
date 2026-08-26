namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// 一斉送信の全宛先共通部分 (IMailSender のコントラクト)。
    /// サーバーが一斉送信リクエストから組み立てる (これ自体は通信に乗らない)。
    /// </summary>
    public class MailBulkTemplate
    {
        /// <summary>差出人アドレス (任意)。空 = 送信者設定の差出人。AllowedFromDomains の検証は単発送信と同じ。</summary>
        public string From { get; set; } = string.Empty;

        /// <summary>差出人表示名 (From 指定時のみ使われる)。</summary>
        public string FromDisplayName { get; set; } = string.Empty;

        /// <summary>件名テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>本文テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        public List<MailAttachment> Attachments { get; set; } = new();
    }
}
