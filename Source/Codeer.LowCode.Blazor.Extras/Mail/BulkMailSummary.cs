using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// One send recorded in a BulkMailField's DB column. The column holds a JSON array of these,
    /// newest first. Failures keep only the first few details (the full audit lives in the
    /// history module when Mail.HistoryModuleName is configured).
    /// </summary>
    public class BulkMailSummaryEntry
    {
        public DateTime SentAt { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<MailSendFailure> Failures { get; set; } = new();
    }

    /// <summary>
    /// Serialization for the BulkMailField summary column. Shared by the server (authoritative write
    /// after a send) and the client (optimistic local refresh and display).
    /// </summary>
    internal static class BulkMailSummary
    {
        public const int MaxEntries = 20;
        public const int MaxFailureDetails = 5;

        public static List<BulkMailSummaryEntry> Parse(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new();
            try
            {
                return JsonSerializer.Deserialize<List<BulkMailSummaryEntry>>(json) ?? new();
            }
            catch (JsonException)
            {
                return new();
            }
        }

        public static string Prepend(string? currentJson, BulkMailSummaryEntry entry)
        {
            var entries = Parse(currentJson);
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            return JsonSerializer.Serialize(entries);
        }

        public static BulkMailSummaryEntry CreateEntry(string sender, string subject, MailSendResult result, DateTime sentAt)
            => new()
            {
                SentAt = sentAt,
                Sender = sender,
                Subject = subject,
                TotalCount = result.TotalCount,
                SuccessCount = result.SuccessCount,
                FailureCount = result.Failures.Count,
                Failures = result.Failures.Take(MaxFailureDetails).ToList(),
            };
    }
}
