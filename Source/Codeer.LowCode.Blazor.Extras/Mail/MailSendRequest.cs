namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>Wire format of POST /api/mail (single send).</summary>
    public class MailSendRequest
    {
        public string MailInfraName { get; set; } = string.Empty;
        public MailMessage Message { get; set; } = new();
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }
}
