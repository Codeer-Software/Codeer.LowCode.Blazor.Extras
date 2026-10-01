using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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
        public async Task SinkFailureLeavesTheWholeRecordInTheApplicationLog()
        {
            //後段が書けなかった操作や BestEffort では、アプリのログが唯一の記録になる。対象・RequestId・詳細まで残す
            var log = new CapturingLogger();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, FailureMode = AuditFailureMode.BestEffort }, [new CapturingAuditSink { Fail = true }], log);
            var e = Event(AuditCategory.DataWrite);
            e.UserId = "u1";
            e.RequestId = "req-1";
            e.ClientIp = "203.0.113.7";
            e.Detail = "reason";
            e.Targets.Add(new AuditTarget { Module = "Order", Id = "302", Operation = "Update" });
            await logger.WriteAsync(e);

            Assert.That(log.Entries, Has.Count.EqualTo(1));
            Assert.That(log.Entries[0].Level, Is.EqualTo(LogLevel.Critical));
            var message = log.Entries[0].Message;
            Assert.That(message, Does.Contain("CapturingAuditSink"));
            //ファイル出力と同じ JSON 1 行
            var json = message[message.IndexOf('{')..];
            var recorded = JsonDocument.Parse(json).RootElement;
            Assert.That(recorded.GetProperty("Category").GetString(), Is.EqualTo("DataWrite"));
            Assert.That(recorded.GetProperty("Action").GetString(), Is.EqualTo("Test.Action"));
            Assert.That(recorded.GetProperty("Result").GetString(), Is.EqualTo("Success"));
            Assert.That(recorded.GetProperty("UserId").GetString(), Is.EqualTo("u1"));
            Assert.That(recorded.GetProperty("RequestId").GetString(), Is.EqualTo("req-1"));
            Assert.That(recorded.GetProperty("ClientIp").GetString(), Is.EqualTo("203.0.113.7"));
            Assert.That(recorded.GetProperty("Detail").GetString(), Is.EqualTo("reason"));
            var target = recorded.GetProperty("Targets")[0];
            Assert.That(target.GetProperty("Module").GetString(), Is.EqualTo("Order"));
            Assert.That(target.GetProperty("Id").GetString(), Is.EqualTo("302"));
            Assert.That(target.GetProperty("Operation").GetString(), Is.EqualTo("Update"));
        }

        [Test]
        public async Task DesignVersionIsEmptyWhenTheHostDoesNotProvideIt()
        {
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink]);
            await logger.WriteAsync(Event(AuditCategory.DataWrite));
            Assert.That(sink.Events.Select(e => (e.Action, e.DesignVersion)).ToArray(), Is.EqualTo(new[] { ("Test.Action", "") }));
        }

        [Test]
        public async Task DesignVersionIsFilled_AndTheSwitchIsRecordedOncePerProcess()
        {
            var current = "v1";
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink], designVersion: _ => current);

            //リクエストの外の記録は今読み込んでいる版。最初の 1 回は切替として書かない (起動の記録が版を持つ)
            await logger.WriteAsync(Event(AuditCategory.System));
            current = "v2";
            await logger.WriteAsync(Event(AuditCategory.DataWrite));
            //切替の前に受け付けたリクエストの結果 (版は v1 のまま)。古い版へ戻す切替の記録は書かない
            var inFlight = Event(AuditCategory.DataWrite);
            inFlight.DesignVersion = "v1";
            await logger.WriteAsync(inFlight);

            Assert.That(sink.Events.Select(e => (e.Action, e.DesignVersion, e.Detail)).ToArray(), Is.EqualTo(new[]
            {
                ("Test.Action", "v1", ""),
                ("Design.Loaded", "v2", "Previous=v1"),
                ("Test.Action", "v2", ""),
                ("Test.Action", "v1", ""),
            }));
            Assert.That(sink.Events[1].Category, Is.EqualTo(AuditCategory.System));
        }

        [Test]
        public async Task TheSwitchIsNoticedEvenWhenTheEventItselfIsNotRecorded()
        {
            //成功した DataRead を記録しない設定でも、切替はその API が来た時点で記録する
            var current = "v1";
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true, Categories = [AuditCategory.System] }, [sink], designVersion: _ => current);
            await logger.WriteAsync(Event(AuditCategory.System));
            current = "v2";
            await logger.WriteAsync(Event(AuditCategory.DataRead));
            Assert.That(sink.Events.Select(e => (e.Action, e.DesignVersion)).ToArray(), Is.EqualTo(new[] { ("Test.Action", "v1"), ("Design.Loaded", "v2") }));
        }

        sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
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
