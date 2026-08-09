namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>One recipient of a bulk send with its substitution variables.</summary>
    public class MailBulkRecipient
    {
        public string To { get; set; } = string.Empty;
        public List<string> Cc { get; set; } = new();
        public List<string> Bcc { get; set; } = new();
        /// <summary>Variable name (without braces) to value.</summary>
        public Dictionary<string, string> Variables { get; set; } = new();
    }
}
