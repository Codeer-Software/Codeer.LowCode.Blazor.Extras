namespace Codeer.LowCode.Blazor.Extras.Api
{
    /// <summary>
    /// Extras の機能 (メール・ジョブなど) がホストの 1 本の API (テンプレの Controllers/ExtrasController.cs) に送る要求。
    /// どの機能のどの操作かを Feature / Action で表し、操作ごとの要求 (MailSendRequest 等) は Payload に JSON で入れる。
    /// </summary>
    public class ExtrasRequest
    {
        /// <summary>機能の名前 (MailApiNames.Feature 等)。</summary>
        public string Feature { get; set; } = string.Empty;

        /// <summary>操作の名前 (MailApiNames.Send 等)。</summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>操作ごとの要求の JSON (本体の JsonConverterEx で読み書きする)。要求を持たない操作は空。</summary>
        public string Payload { get; set; } = string.Empty;
    }
}
