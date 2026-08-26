namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>一斉送信の宛先1件 (差し込み変数付き)。</summary>
    public class MailBulkRecipient
    {
        public string To { get; set; } = string.Empty;
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        /// <summary>変数名 (中括弧なし) → 値。</summary>
        public Dictionary<string, string> Variables { get; set; } = new();
    }
}
