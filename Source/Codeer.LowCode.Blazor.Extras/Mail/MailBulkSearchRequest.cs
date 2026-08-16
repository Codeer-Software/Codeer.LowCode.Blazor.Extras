using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// POST /api/mail/bulk_search のワイヤ形式 (宛先は検索条件からサーバーで解決する。
    /// アドレスはクライアントに渡らない)。
    /// </summary>
    public class MailBulkSearchRequest
    {
        public string MailInfraName { get; set; } = string.Empty;
        /// <summary>差出人アドレス (任意)。空 = 送信者設定の差出人。AllowedFromDomains で許可されたドメインのみ。</summary>
        public string From { get; set; } = string.Empty;
        /// <summary>差出人表示名 (From 指定時のみ使われる)。</summary>
        public string FromDisplayName { get; set; } = string.Empty;
        /// <summary>件名テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>本文テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        /// <summary>全宛先共通の添付ファイル。</summary>
        public List<MailAttachment> Attachments { get; set; } = new();
        public SearchCondition Condition { get; set; } = new();
        /// <summary>宛先行モジュールの、メールアドレスを持つ変数。リンクパス ("Contact.Email") と末尾 ".Value" 可。</summary>
        public string EmailAddressVariable { get; set; } = string.Empty;
        /// <summary>宛先行モジュールの Boolean 変数。true の行は除外 (配信停止)。表記は EmailAddressVariable と同じ。</summary>
        public string OptOutVariable { get; set; } = string.Empty;
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        /// <summary>
        /// 送信結果サマリを保存する、起点レコードの BulkMailField 名。設定時 (かつサーバーに
        /// 内部更新経路が結線されているとき)、送信後にそのフィールドの DB 列へサマリ JSON が書き戻される。
        /// </summary>
        public string SummaryFieldName { get; set; } = string.Empty;
    }
}
