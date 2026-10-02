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
        public async Task WritesWhileAnotherProcessHoldsTheFileOpen()
        {
            //同じホストの別プロセス (IIS のオーバーラップリサイクル中の新旧ワーカー) が同じファイルを追記用に開いていても書ける。
            //別プロセスが開いている状態を、他の書き込みを許す追記用ハンドルで作る
            var day = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
            Directory.CreateDirectory(_dir);
            var path = Path.Combine(_dir, "audit-h-20260930.jsonl");
            await using (var other = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                await other.WriteAsync("{\"Action\":\"other\"}\n"u8.ToArray());
                await other.FlushAsync();

                var sink = new FileAuditSink(_dir);
                await sink.WriteAsync(new AuditEvent { OccurredAtUtc = day, Host = "h", Action = "Mine" });
            }

            var lines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Does.Contain("\"other\""));
            Assert.That(lines[1], Does.Contain("\"Mine\""));
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
