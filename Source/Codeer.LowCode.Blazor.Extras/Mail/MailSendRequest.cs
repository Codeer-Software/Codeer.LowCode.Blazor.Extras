namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// POST /api/mail (単発送信) のワイヤ形式。
    /// サーバーは SourceModule / FieldName の MailField があり、そのフィールドを今のユーザーが読めること (モジュールの UserRead・フィールド読取権限) を確かめてから送る (行は読まない)。
    /// 送信インフラの呼び名はデザインの MailInfraName を使う (MailInfraName はクライアントの参考値)。
    /// </summary>
    public class MailSendRequest
    {
        public string MailInfraName { get; set; } = string.Empty;

        public MailMessage Message { get; set; } = new();
        /// <summary>MailField を置いたモジュール名 (= 送信履歴の SourceModule)。</summary>
        public string SourceModule { get; set; } = string.Empty;
        /// <summary>送信元レコードの Id (= 送信履歴の SourceId)。未保存なら空。</summary>
        public string SourceId { get; set; } = string.Empty;
        /// <summary>MailField のフィールド名。</summary>
        public string FieldName { get; set; } = string.Empty;
    }
}
