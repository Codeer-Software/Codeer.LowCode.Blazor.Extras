namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>POST /api/mail (単発送信) のワイヤ形式。</summary>
    public class MailSendRequest
    {
        public string MailInfraName { get; set; } = string.Empty;

        public MailMessage Message { get; set; } = new();
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }
}
