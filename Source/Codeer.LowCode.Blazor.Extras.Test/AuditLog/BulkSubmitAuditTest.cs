using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    /// <summary>
    /// スクリプトの一括保存 (bulk_submit) の監査。結果は BulkFileTransfer の中で JSON になるので、
    /// 対象のモジュールと保存結果のエラーは <see cref="BulkFileTransfer.BulkSubmitAsync"/> に渡した AuditContext に入る。実 DB (SQLite) で通す。
    /// </summary>
    public class BulkSubmitAuditTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = [];
        DbAccessor _db = null!;
        //保存に使う接続。本番と同じくリクエストごとの新しい DbAccessor (先に開いた接続にはトランザクションが掛からない)
        DbAccessor _ioDb = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "items" };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            item.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            item.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(item);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"audit_bulk_test_{Guid.NewGuid():N}.db");
            _dataSources = [new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }];
            _db = new DbAccessor(_dataSources);
            _ioDb = new DbAccessor(_dataSources);
            await _db.ExecuteAsync(Ds, "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO items (id, name) VALUES (10, 'A')", new());
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            await _ioDb.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        ModuleDataIO CreateIO(DesignData design) => new(design, this, _ioDb, new TemporaryFileManager(_ioDb, [], new List<IFileStorage>()));

        static MemoryStream Body(params string[] names)
        {
            var rows = names.Select(n => new ModuleData { Name = "Item", Fields = { ["Name"] = new TextFieldData { Value = n } } }).ToList();
            return new MemoryStream(Encoding.UTF8.GetBytes(JsonConverterEx.SerializeObject(rows)));
        }

        async Task<long> CountAsync() => (long)(await _db.QueryAsync(Ds, "SELECT count(*) AS c FROM items", new()))[0]["c"]!;

        [Test]
        public async Task SuccessRecordsTheModule()
        {
            var design = CreateDesign();
            var audit = new AuditContext();

            await BulkFileTransfer.BulkSubmitAsync(CreateIO(design), "Item", Body("B", "C"), audit);

            Assert.That(await CountAsync(), Is.EqualTo(3));
            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Item", (string?)null, "BulkSubmit") }));
        }

        [Test]
        public async Task SaveErrorIsRecordedAsFailure()
        {
            var design = CreateDesign();
            var audit = new AuditContext();

            //name は UNIQUE。既存の 'A' と重なるので保存は失敗し、全体がロールバックする
            await BulkFileTransfer.BulkSubmitAsync(CreateIO(design), "Item", Body("B", "A"), audit);

            Assert.That(await CountAsync(), Is.EqualTo(1));
            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Failure));
            Assert.That(audit.Event.Detail, Is.Not.Empty);
            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Item", "BulkSubmit") }));
        }
    }
}
