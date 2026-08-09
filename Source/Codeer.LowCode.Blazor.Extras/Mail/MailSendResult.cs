namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>Result of a send operation. Partial failures are reported per recipient.</summary>
    public class MailSendResult
    {
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public List<MailSendFailure> Failures { get; set; } = new();
        public bool IsSuccess => Failures.Count == 0;

        internal static MailSendResult Success(int count) => new() { TotalCount = count, SuccessCount = count };

        internal static MailSendResult Failure(string to, string error) => new()
        {
            TotalCount = 1,
            Failures = { new MailSendFailure { To = to, Error = error } }
        };
    }

    public class MailSendFailure
    {
        public string To { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }
}
