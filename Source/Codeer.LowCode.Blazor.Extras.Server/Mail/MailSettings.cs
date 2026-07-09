namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    public class MailSettings
    {
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string SenderMailAddress { get; set; } = string.Empty;
        public string SenderDisplayName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string SSL { get; set; } = string.Empty;
    }
}
