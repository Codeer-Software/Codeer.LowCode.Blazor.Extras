using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// POST /api/mail/bulk_search のワイヤ形式 (宛先は検索条件からサーバーで解決する。
    /// アドレスはクライアントに渡らない)。
    /// どの値がアドレス・配信停止かは**宛先行モジュールの BulkMailRecipientContractField** をサーバーが読む
    /// (クライアントからは指定できない)。
    /// </summary>
    public class MailBulkSearchRequest
    {
        public string MailInfraName { get; set; } = string.Empty;
        /// <summary>自分 (操作ユーザー) を差出人にする。差出人はクライアント値でなくサーバーが解決する (なりすましの構造的排除)。</summary>
        public bool IsFromCurrentUser { get; set; }
        /// <summary>件名テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>本文テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        /// <summary>全宛先共通の添付ファイル。</summary>
        public List<MailAttachment> Attachments { get; set; } = new();
        public SearchCondition Condition { get; set; } = new();
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        /// <summary>
        /// 送信結果サマリを保存する、起点レコードの BulkMailField 名。設定時 (かつサーバーに
        /// 内部更新経路が結線されているとき)、送信後にそのフィールドの DB 列へサマリ JSON が書き戻される。
        /// </summary>
        public string SummaryFieldName { get; set; } = string.Empty;
    }
}
