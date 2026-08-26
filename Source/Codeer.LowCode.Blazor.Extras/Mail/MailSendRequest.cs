namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>POST /api/mail (単発送信) のワイヤ形式。</summary>
    public class MailSendRequest
    {
        public string MailInfraName { get; set; } = string.Empty;

        /// <summary>自分 (操作ユーザー) を差出人にする。差出人はクライアント値でなくサーバーが解決する (なりすましの構造的排除)。</summary>
        public bool IsFromCurrentUser { get; set; }

        public MailMessage Message { get; set; } = new();
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }
}
