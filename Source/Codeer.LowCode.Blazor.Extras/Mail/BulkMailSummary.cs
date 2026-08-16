using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// BulkMailField の DB 列に記録される送信1件。列はこの JSON 配列 (新しい順) を持つ。
    /// 失敗の明細は先頭数件だけ残す (全量の監査記録は Mail.HistoryModuleName 設定時の履歴モジュール)。
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
    /// BulkMailField サマリ列のシリアライズ。サーバー (送信後の正値書き込み) と
    /// クライアント (楽観的なローカル表示更新) で共有。
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
