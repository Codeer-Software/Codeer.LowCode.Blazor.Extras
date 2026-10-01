using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    public class AuditLoggerTest
    {
        static AuditEvent Event(AuditCategory category, AuditResult result = AuditResult.Success)
            => new() { Category = category, Action = "Test.Action", Result = result };

        [Test]
        public void EnabledWithoutSinkIsAConfigurationError()
        {
            Assert.Throws<InvalidOperationException>(() => new AuditLogger(new AuditLogSettings { Enabled = true }, []));
            Assert.DoesNotThrow(() => new AuditLogger(new AuditLogSettings { Enabled = false }, []));
        }

        [Test]
        public async Task DisabledWritesNothing()
        {
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = false }, [sink]);
            await logger.WriteAsync(Event(AuditCategory.DataWrite));
            Assert.That(sink.Events, Is.Empty);
        }

        [Test]
        public async Task CategoriesFilterSuccessOnly()
        {
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, Categories = [AuditCategory.DataWrite] }, [sink]);
            await logger.WriteAsync(Event(AuditCategory.DataWrite));
            await logger.WriteAsync(Event(AuditCategory.DataRead));
            await logger.WriteAsync(Event(AuditCategory.DataRead, AuditResult.Failure));
            await logger.WriteAsync(Event(AuditCategory.Other, AuditResult.Denied));
            Assert.That(sink.Events.Select(e => (e.Category, e.Result)).ToArray(), Is.EqualTo(new[]
            {
                (AuditCategory.DataWrite, AuditResult.Success),
                (AuditCategory.DataRead, AuditResult.Failure),
                (AuditCategory.Other, AuditResult.Denied),
            }));
        }

        [Test]
        public void AttemptFollowsBothCategoriesAndAttemptCategories()
        {
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, Categories = [AuditCategory.DataWrite, AuditCategory.DataRead] }, [sink]);
            Assert.That(logger.ShouldRecord(Event(AuditCategory.DataWrite, AuditResult.Attempt)), Is.True, "既定の AttemptCategories に DataWrite がある");
            Assert.That(logger.ShouldRecord(Event(AuditCategory.DataRead, AuditResult.Attempt)), Is.False, "DataRead には前段を付けない");
            Assert.That(logger.ShouldRecord(Event(AuditCategory.Export, AuditResult.Attempt)), Is.False, "Categories に無い分類は前段も書かない");
            var none = new AuditLogger(new AuditLogSettings { Enabled = true, AttemptCategories = [] }, [sink]);
            Assert.That(none.ShouldRecord(Event(AuditCategory.DataWrite, AuditResult.Attempt)), Is.False);
        }

        [Test]
        public async Task EmptyCategoriesMeansAll()
        {
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink]);
            await logger.WriteAsync(Event(AuditCategory.Other));
            Assert.That(sink.Events, Has.Count.EqualTo(1));
        }

        [Test]
        public void StrictThrowsWhenAnySinkFails_ButStillWritesTheOthers()
        {
            var ok = new CapturingAuditSink();
            var down = new CapturingAuditSink { Fail = true };
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, FailureMode = AuditFailureMode.Strict }, [down, ok]);
            var ex = Assert.ThrowsAsync<AuditLogException>(() => logger.WriteAsync(Event(AuditCategory.DataWrite)));
            Assert.That(ex!.InnerException, Is.InstanceOf<IOException>());
            Assert.That(ok.Events, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BestEffortSwallowsSinkFailure()
        {
            var ok = new CapturingAuditSink();
            var down = new CapturingAuditSink { Fail = true };
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, FailureMode = AuditFailureMode.BestEffort }, [down, ok]);
            await logger.WriteAsync(Event(AuditCategory.DataWrite));
            Assert.That(ok.Events, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PurgeRecordsCountsAsSystemEvent()
        {
            var sink = new CapturingAuditSink { Purged = 12 };
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, RetentionDays = 30 }, [sink]);
            await logger.PurgeAsync();
            Assert.That(sink.Events, Has.Count.EqualTo(1));
            Assert.That(sink.Events[0].Category, Is.EqualTo(AuditCategory.System));
            Assert.That(sink.Events[0].Action, Is.EqualTo("AuditLog.Purge"));
            Assert.That(sink.Events[0].Detail, Does.Contain("CapturingAuditSink=12"));
        }

        [Test]
        public async Task PurgeIsSkippedWithoutRetention()
        {
            var sink = new CapturingAuditSink { Purged = 12 };
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, RetentionDays = 0 }, [sink]);
            await logger.PurgeAsync();
            Assert.That(sink.Events, Is.Empty);
        }
    }
}
