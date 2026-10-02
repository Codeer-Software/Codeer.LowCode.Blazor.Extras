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
        public async Task AttributedSuccessIsRecorded_OtherOnlyWhenItFailsOrIsDenied()
        {
            //何を記録するかは設定ではなくコード (分類の宣言) が決める。宣言の無い Other は失敗と拒否だけ
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink]);
            await logger.WriteAsync(Event(AuditCategory.DataWrite));
            await logger.WriteAsync(Event(AuditCategory.DataRead));
            await logger.WriteAsync(Event(AuditCategory.Other));
            await logger.WriteAsync(Event(AuditCategory.Other, AuditResult.Failure));
            await logger.WriteAsync(Event(AuditCategory.Other, AuditResult.Denied));
            Assert.That(sink.Events.Select(e => (e.Category, e.Result)).ToArray(), Is.EqualTo(new[]
            {
                (AuditCategory.DataWrite, AuditResult.Success),
                (AuditCategory.DataRead, AuditResult.Success),
                (AuditCategory.Other, AuditResult.Failure),
                (AuditCategory.Other, AuditResult.Denied),
            }));
        }

        [Test]
        public void AttemptIsFixedByCategory()
        {
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [new CapturingAuditSink()]);
            foreach (var category in new[] { AuditCategory.Authentication, AuditCategory.DataWrite, AuditCategory.Export, AuditCategory.Admin })
                Assert.That(logger.ShouldRecord(Event(category, AuditResult.Attempt)), Is.True, $"{category} は二段");
            foreach (var category in new[] { AuditCategory.DataRead, AuditCategory.System, AuditCategory.Other })
                Assert.That(logger.ShouldRecord(Event(category, AuditResult.Attempt)), Is.False, $"{category} には前段を付けない");
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
            //記録しないレコード (Other の成功) でも、切替はその API が来た時点で記録する
            var current = "v1";
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink], designVersion: _ => current);
            await logger.WriteAsync(Event(AuditCategory.System));
            current = "v2";
            await logger.WriteAsync(Event(AuditCategory.Other));
            Assert.That(sink.Events.Select(e => (e.Action, e.DesignVersion)).ToArray(), Is.EqualTo(new[] { ("Test.Action", "v1"), ("Design.Loaded", "v2") }));
        }

        [Test]
        public async Task ManyTargetsAreSplitIntoContinuationRecords()
        {
            //1 レコードの対象は MaxTargetsPerRecord 件まで。超えた分は切り捨てず、同じ RequestId の続きの行 (Continued) に分ける
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink]);
            var e = Event(AuditCategory.DataWrite);
            e.RequestId = "req-1";
            e.UserId = "u1";
            e.Detail = "Add=1201; Update=0; Delete=0";
            for (var i = 0; i < 1201; i++) e.Targets.Add(new AuditTarget { Module = "Item", Id = i.ToString(), Operation = "Add" });

            await logger.WriteAsync(e);

            Assert.That(sink.Events.Select(x => (x.Result, x.Targets.Count, x.Detail)).ToArray(), Is.EqualTo(new[]
            {
                (AuditResult.Success, 500, "Add=1201; Update=0; Delete=0"),
                (AuditResult.Continued, 500, ""),
                (AuditResult.Continued, 201, ""),
            }));
            Assert.That(sink.Events.All(x => x.RequestId == "req-1" && x.UserId == "u1" && x.Action == "Test.Action" && x.Category == AuditCategory.DataWrite), Is.True);
            Assert.That(sink.Events.SelectMany(x => x.Targets).Select(t => t.Id).ToArray(), Is.EqualTo(Enumerable.Range(0, 1201).Select(i => i.ToString()).ToArray()), "対象は 1 件も欠けない");
        }

        [Test]
        public async Task ContinuationRecordsOutsideARequestAreTiedByAGeneratedId()
        {
            //リクエストの外の記録には RequestId が無い。続きの行と結べるよう、分けるときに付ける
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink]);
            var e = Event(AuditCategory.DataWrite);
            for (var i = 0; i < 501; i++) e.Targets.Add(new AuditTarget { Module = "Item", Id = i.ToString(), Operation = "Add" });

            await logger.WriteAsync(e);

            Assert.That(sink.Events, Has.Count.EqualTo(2));
            Assert.That(sink.Events[0].RequestId, Is.Not.Empty.And.EqualTo(sink.Events[1].RequestId));
        }

        [Test]
        public async Task UpToTheLimitIsWrittenAsOneRecord()
        {
            var sink = new CapturingAuditSink();
            var logger = new AuditLogger(new AuditLogSettings { Enabled = true }, [sink]);
            var e = Event(AuditCategory.DataWrite);
            for (var i = 0; i < AuditLogger.MaxTargetsPerRecord; i++) e.Targets.Add(new AuditTarget { Module = "Item", Id = i.ToString(), Operation = "Add" });
            await logger.WriteAsync(e);
            Assert.That(sink.Events, Is.EqualTo(new[] { e }));
        }

        sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
