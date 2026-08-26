using Codeer.LowCode.Blazor.Extras.Mail;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class BulkMailSummaryTest
    {
        static MailSendResult CreateResult(int total, int failureCount)
        {
            var result = new MailSendResult { TotalCount = total, SuccessCount = total - failureCount };
            for (var i = 0; i < failureCount; i++)
            {
                result.Failures.Add(new MailSendFailure { To = $"x{i}@example.com", Error = "invalid" });
            }
            return result;
        }

        [Test]
        public void Prepend_新しい順に積まれ上限で切り詰められる()
        {
            var json = string.Empty;
            for (var i = 0; i < BulkMailSummary.MaxEntries + 5; i++)
            {
                json = BulkMailSummary.Prepend(json, BulkMailSummary.CreateEntry("Notify", $"件名{i}",
                    CreateResult(10, 0), new DateTime(2026, 8, 9, 12, 0, 0).AddMinutes(i)));
            }

            var entries = BulkMailSummary.Parse(json);
            Assert.That(entries.Count, Is.EqualTo(BulkMailSummary.MaxEntries));
            Assert.That(entries[0].Subject, Is.EqualTo($"件名{BulkMailSummary.MaxEntries + 4}")); //先頭が最新
        }

        [Test]
        public void CreateEntry_失敗明細は先頭数件だけ全数はFailureCount()
        {
            var entry = BulkMailSummary.CreateEntry("Notify", "件名",
                CreateResult(100, BulkMailSummary.MaxFailureDetails + 3), new DateTime(2026, 8, 9));

            Assert.That(entry.TotalCount, Is.EqualTo(100));
            Assert.That(entry.SuccessCount, Is.EqualTo(100 - BulkMailSummary.MaxFailureDetails - 3));
            Assert.That(entry.FailureCount, Is.EqualTo(BulkMailSummary.MaxFailureDetails + 3));
            Assert.That(entry.Failures.Count, Is.EqualTo(BulkMailSummary.MaxFailureDetails));
        }

        [Test]
        public void Parse_不正なJSONと空は空リスト()
        {
            Assert.That(BulkMailSummary.Parse(null), Is.Empty);
            Assert.That(BulkMailSummary.Parse(""), Is.Empty);
            Assert.That(BulkMailSummary.Parse("{broken"), Is.Empty);
        }
    }
}
