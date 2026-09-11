using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// POST /api/mail/bulk_search のワイヤ形式 (宛先は検索条件からサーバーで解決する。
    /// アドレスはクライアントに渡らない)。
    /// どの値がアドレス・配信停止かは**宛先行モジュールの BulkMailRecipientContractField** をサーバーが読む
    /// (クライアントからは指定できない)。
    /// サーバーは SourceModule / FieldName の BulkMailField があり、そのフィールドを今のユーザーが読めること (モジュールの UserRead・フィールド読取権限) を確かめてから宛先を解決する。
    /// 送信インフラの呼び名はデザインの MailInfraName を使う (MailInfraName はクライアントの参考値)。
    /// </summary>
    public class MailBulkSearchRequest
    {
        public string MailInfraName { get; set; } = string.Empty;
        /// <summary>件名テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Subject { get; set; } = string.Empty;
        /// <summary>本文テンプレート。{変数} は宛先ごとに差し込まれる。</summary>
        public string Body { get; set; } = string.Empty;
        public bool IsBodyHtml { get; set; }
        public string ReplyTo { get; set; } = string.Empty;
        /// <summary>全宛先共通の添付ファイル。</summary>
        public List<MailAttachment> Attachments { get; set; } = new();
        public SearchCondition Condition { get; set; } = new();
        /// <summary>BulkMailField を置いたモジュール名 (= 送信履歴の SourceModule)。</summary>
        public string SourceModule { get; set; } = string.Empty;
        /// <summary>送信元レコードの Id (= 送信履歴の SourceId)。</summary>
        public string SourceId { get; set; } = string.Empty;
        /// <summary>BulkMailField のフィールド名。</summary>
        public string FieldName { get; set; } = string.Empty;
    }
}
