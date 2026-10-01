using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    public class FileAuditSinkTest
    {
        string _dir = string.Empty;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "clb_audit_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        [Test]
        public async Task WritesOneJsonLinePerEvent_PerHostAndDay()
        {
            var sink = new FileAuditSink(_dir);
            var day = new DateTime(2026, 9, 30, 1, 2, 3, DateTimeKind.Utc);
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = day, Host = "web-1", Category = AuditCategory.DataWrite, Action = "ModuleData.Submit", UserId = "u1", Targets = { new AuditTarget { Module = "Customer", Id = "7", Operation = "Update" } } });
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = day, Host = "web-1", Category = AuditCategory.Authentication, Action = "Account.Login", Result = AuditResult.Denied });

            var path = Path.Combine(_dir, "audit-web_1-20260930.jsonl");
            Assert.That(File.Exists(path));
            var lines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines, Has.Length.EqualTo(2));
            using var doc = JsonDocument.Parse(lines[0]);
            Assert.That(doc.RootElement.GetProperty("Category").GetString(), Is.EqualTo("DataWrite"));
            Assert.That(doc.RootElement.GetProperty("OccurredAtUtc").GetString(), Does.StartWith("2026-09-30T01:02:03"));
            Assert.That(doc.RootElement.GetProperty("Targets")[0].GetProperty("Id").GetString(), Is.EqualTo("7"));
            using var doc2 = JsonDocument.Parse(lines[1]);
            Assert.That(doc2.RootElement.GetProperty("Result").GetString(), Is.EqualTo("Denied"));
        }

        [Test]
        public async Task PurgeDeletesOnlyFilesOlderThanCutoff()
        {
            var sink = new FileAuditSink(_dir);
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Host = "h" });
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), Host = "h" });
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Host = "h" });

            var deleted = await sink.PurgeAsync(new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc));

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToArray(), Is.EqualTo(new[] { "audit-h-20260201.jsonl", "audit-h-20260301.jsonl" }));
        }
    }

    public class DatabaseAuditSinkTest
    {
        const string Ds = "Audit";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = [];

        [SetUp]
        public async Task SetUp()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), "clb_audit_" + Guid.NewGuid().ToString("N") + ".db");
            _dataSources = [new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }];
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, DatabaseAuditSink.CreateTableSql(DataSourceType.SQLite, "audit_log"), new());
        }

        [TearDown]
        public void TearDown() { if (File.Exists(_dbFile)) File.Delete(_dbFile); }

        DatabaseAuditSink CreateSink() => new(new AuditLogDatabaseSettings { DataSourceName = Ds, Table = "audit_log" }, () => new DbAccessor(_dataSources));

        [Test]
        public async Task WritesAllColumns()
        {
            var sink = CreateSink();
            var at = new DateTime(2026, 9, 30, 1, 2, 3, DateTimeKind.Utc);
            await sink.WriteAsync(new AuditEvent
            {
                OccurredAtUtc = at, Category = AuditCategory.DataWrite, Action = "ModuleData.Submit", Result = AuditResult.Failure,
                UserId = "u1", ClientIp = "10.0.0.1", UserAgent = "ua", RequestId = "req", Host = "web-1", Detail = "boom",
                DesignVersion = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                Targets = { new AuditTarget { Module = "Customer", Id = "7", Operation = "Update" }, new AuditTarget { Module = "Order", Operation = "SearchDelete" } },
            });

            await using var db = new DbAccessor(_dataSources);
            var rows = await db.QueryAsync(Ds, "select * from audit_log", new());
            Assert.That(rows, Has.Count.EqualTo(1));
            var row = rows[0];
            Assert.That(row["category"], Is.EqualTo("DataWrite"));
            Assert.That(row["action"], Is.EqualTo("ModuleData.Submit"));
            Assert.That(row["result"], Is.EqualTo("Failure"));
            Assert.That(row["user_id"], Is.EqualTo("u1"));
            Assert.That(row["client_ip"], Is.EqualTo("10.0.0.1"));
            Assert.That(row["user_agent"], Is.EqualTo("ua"));
            Assert.That(row["request_id"], Is.EqualTo("req"));
            Assert.That(row["host"], Is.EqualTo("web-1"));
            Assert.That(row["design_version"], Is.EqualTo("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
            Assert.That(row["detail"], Is.EqualTo("boom"));
            Assert.That(row["targets"]!.ToString(), Is.EqualTo("[{\"Module\":\"Customer\",\"Id\":\"7\",\"Operation\":\"Update\"},{\"Module\":\"Order\",\"Operation\":\"SearchDelete\"}]"));
            Assert.That(row["occurred_at_utc"]!.ToString(), Does.StartWith("2026-09-30 01:02:03"));
        }

        [Test]
        public async Task PurgeDeletesOlderRows()
        {
            var sink = CreateSink();
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Action = "old" });
            await sink.WriteAsync(new AuditEvent { OccurredAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), Action = "new" });

            var deleted = await sink.PurgeAsync(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.That(deleted, Is.EqualTo(1));
            await using var db = new DbAccessor(_dataSources);
            var rows = await db.QueryAsync(Ds, "select action from audit_log", new());
            Assert.That(rows.Select(r => r["action"]).ToArray(), Is.EqualTo(new[] { "new" }));
        }

        [Test]
        public void CreateTableSqlPerDatabase()
        {
            Assert.That(DatabaseAuditSink.CreateTableSql(DataSourceType.SQLServer, "audit_log"), Does.Contain("[id] bigint identity(1,1) primary key").And.Contain("nvarchar(max)"));
            Assert.That(DatabaseAuditSink.CreateTableSql(DataSourceType.PostgreSQL, "audit_log"), Does.Contain("\"id\" bigserial primary key"));
            Assert.That(DatabaseAuditSink.CreateTableSql(DataSourceType.MySQL, "audit_log"), Does.Contain("`id` bigint auto_increment primary key")
                .And.Contain("`targets` longtext").And.Contain("`detail` longtext"), "MySQL の text は 64KB までで、行の多い対象が入らない");
            Assert.That(DatabaseAuditSink.CreateTableSql(DataSourceType.Oracle, "audit_log"), Does.Contain("generated always as identity").And.Contain("clob"));
        }
    }
}
