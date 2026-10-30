namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>メールの機能が共通の API (ExtrasTransport / Extras.Server の MailApi) で使う機能名と操作名。</summary>
    public static class MailApiNames
    {
        public const string Feature = "Mail";

        /// <summary>単発送信。要求 MailSendRequest、応答 MailSendResult。</summary>
        public const string Send = "Send";

        /// <summary>一斉送信 (宛先はサーバーで検索条件から解決)。要求 MailBulkSearchRequest、応答 MailSendResult。</summary>
        public const string SendBulkSearch = "SendBulkSearch";

        /// <summary>単発送信のプレビュー。要求 MailPreviewRequest、応答 HTML の文字列。</summary>
        public const string Preview = "Preview";

        /// <summary>一斉送信のプレビュー。要求 MailBulkSearchRequest、応答 HTML の文字列。</summary>
        public const string PreviewBulkSearch = "PreviewBulkSearch";
    }
}
