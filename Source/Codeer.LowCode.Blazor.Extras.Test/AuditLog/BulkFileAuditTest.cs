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
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Excel.Report.PDF;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    /// <summary>
    /// 一括系 (ファイル取込・スクリプトの一括保存・ファイル出力) の監査。実 DB (SQLite) で、本番と同じく
    /// 保存の合流点のインターセプタ (<see cref="AuditIOInterceptor"/>) と今のリクエストの監査レコード (<see cref="AuditContext.Current"/>) を通す。
    /// - 保存: 行ごとの Add / Update と Id、件数。本体の一括 INSERT で入った新規行は Id 無し (モジュールごとに 1 件と件数)
    /// - 取込: 取り込んだファイルの SHA-256
    /// - 出力: 出した行の Id と件数
    /// </summary>
    public class BulkFileAuditTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = [];
        DbAccessor _db = null!;
        //保存に使う接続。本番と同じくリクエストごとの新しい DbAccessor (先に開いた接続にはトランザクションが掛からない)
        DbAccessor _ioDb = null!;
        AuditContext _audit = null!;
        int _bulkAddThreshold;

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
            _bulkAddThreshold = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = -1;
            _audit = new AuditContext();
        }

        //リクエストの間だけ見える監査レコード (本番はミドルウェアが入れる)。非同期の SetUp で入れてもテスト本体には伝わらないので、各テストの先頭で入れる
        void BeginRequest() => AuditContext.Current = _audit;

        [TearDown]
        public async Task TearDown()
        {
            AuditContext.Current = null;
            ModuleDataIO.BulkAddThreshold = _bulkAddThreshold;
            await _db.DisposeAsync();
            await _ioDb.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        //ホストの CustomizedModuleDataIO と同じ結線
        ModuleDataIO CreateIO(DesignData design)
        {
            var io = new ModuleDataIO(design, this, _ioDb, new TemporaryFileManager(_ioDb, [], new List<IFileStorage>()));
            io.AddInterceptor(new AuditIOInterceptor(design, new AuditLogDatabaseSettings()));
            return io;
        }

        static MemoryStream Body(params string[] names)
        {
            var rows = names.Select(n => new ModuleData { Name = "Item", Fields = { ["Name"] = new TextFieldData { Value = n } } }).ToList();
            return new MemoryStream(Encoding.UTF8.GetBytes(JsonConverterEx.SerializeObject(rows)));
        }

        static MemoryStream Xlsx(List<List<string>> texts)
        {
            var file = ExcelUtils.CreateExcelBinary(texts, "data");
            file.Position = 0;
            return file;
        }

        async Task<List<string>> IdsAsync(params string[] names)
        {
            var rows = await _db.QueryAsync(Ds, "SELECT id, name FROM items", new());
            return names.Select(n => rows.Single(r => (string)r["name"]! == n)["id"]!.ToString()!).ToList();
        }

        async Task<long> CountAsync() => (long)(await _db.QueryAsync(Ds, "SELECT count(*) AS c FROM items", new()))[0]["c"]!;

        (string Module, string? Id, string Operation)[] Targets => _audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray();

        [Test]
        public async Task BulkSubmitRecordsEachRowWithItsAssignedId()
        {
            BeginRequest();
            var design = CreateDesign();

            await BulkFileTransfer.BulkSubmitAsync(CreateIO(design), "Item", Body("B", "C"));

            Assert.That(await CountAsync(), Is.EqualTo(3));
            var ids = await IdsAsync("B", "C");
            Assert.That(_audit.Event.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(Targets, Is.EqualTo(new[] { ("Item", (string?)null, "BulkSubmit"), ("Item", ids[0], "Add"), ("Item", ids[1], "Add") }));
            Assert.That(_audit.ComposeDetail(), Is.EqualTo("Add=2; Update=0; Delete=0"));
        }

        [Test]
        public async Task BulkInsertPathRecordsTheCountWithoutIds()
        {
            BeginRequest();
            //本体の一括 INSERT (しきい値以上の純粋な追加) は採番 Id を返さない。監査のために一括 INSERT を止めない = 件数だけが残る
            ModuleDataIO.BulkAddThreshold = 2;
            var design = CreateDesign();

            await BulkFileTransfer.BulkSubmitAsync(CreateIO(design), "Item", Body("B", "C", "D"));

            Assert.That(await CountAsync(), Is.EqualTo(4), "一括 INSERT で入っている");
            Assert.That(_audit.Event.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(Targets, Is.EqualTo(new[] { ("Item", (string?)null, "BulkSubmit"), ("Item", (string?)null, "Add") }));
            Assert.That(_audit.ComposeDetail(), Is.EqualTo("Add=3; Update=0; Delete=0"));
        }

        [Test]
        public async Task SaveErrorIsRecordedAsFailure()
        {
            BeginRequest();
            var design = CreateDesign();

            //name は UNIQUE。既存の 'A' と重なるので保存は失敗し、全体がロールバックする
            await BulkFileTransfer.BulkSubmitAsync(CreateIO(design), "Item", Body("B", "A"));

            Assert.That(await CountAsync(), Is.EqualTo(1));
            Assert.That(_audit.Event.Result, Is.EqualTo(AuditResult.Failure));
            Assert.That(_audit.Event.Detail, Is.Not.Empty);
            //ロールバックしたので採番 Id は無い。試みた件数は残る
            Assert.That(Targets, Is.EqualTo(new[] { ("Item", (string?)null, "BulkSubmit"), ("Item", (string?)null, "Add") }));
            Assert.That(_audit.ComposeDetail(), Does.StartWith("Add=2; Update=0; Delete=0; "));
        }

        [Test]
        public async Task FileImportRecordsRowsCountsAndTheFileHash()
        {
            BeginRequest();
            var design = CreateDesign();
            var file = Xlsx(
            [
                ["Id.Value", "Name.Value"],
                ["10", "A2"],
                ["", "B"],
            ]);
            var hash = Convert.ToHexString(SHA256.HashData(file.ToArray())).ToLowerInvariant();

            var results = await BulkFileTransfer.SubmitByFileAsync(design, CreateIO(design), "Item", file);

            Assert.That(results.Select(r => r.ExceptionMessage).Where(m => !string.IsNullOrEmpty(m)), Is.Empty);
            //取込の応答は監査ログが無いときと同じ (監査のために付けた仮 Id は残らない)
            Assert.That(results.Select(r => (r.SourceId, r.DestinationId, r.TemporaryIdMap.Count)).ToArray(), Is.EquivalentTo(new[] { ("10", "10", 0), ("", "", 0) }));
            var added = (await IdsAsync("B"))[0];
            Assert.That(Targets, Is.EquivalentTo(new[] { ("Item", (string?)"10", "Update"), ("Item", added, "Add") }));
            //取り込んだファイルは SHA-256 で特定できる (行の中身の証拠は取込ファイル)
            Assert.That(_audit.ComposeDetail(), Is.EqualTo($"Add=1; Update=1; Delete=0; File={hash}"));
        }

        [Test]
        public async Task ExportRecordsTheIdsOfTheRowsAndTheCount()
        {
            BeginRequest();
            var design = CreateDesign();
            await _db.ExecuteAsync(Ds, "INSERT INTO items (id, name) VALUES (11, 'B')", new());

            await BulkFileTransfer.GetListFileAsync(design, CreateIO(design), new SearchCondition { ModuleName = "Item" });

            Assert.That(Targets, Is.EquivalentTo(new[] { ("Item", (string?)"10", "Export"), ("Item", (string?)"11", "Export") }));
            Assert.That(_audit.ComposeDetail(), Is.EqualTo("Rows=2"));
        }

        [Test]
        public async Task NothingIsRecordedOutsideARequest()
        {
            //バックグラウンドのジョブ・監査ログが無効のとき (Current が無い) も、取込と出力はそのまま動く
            var design = CreateDesign();

            await BulkFileTransfer.SubmitByFileAsync(design, CreateIO(design), "Item", Xlsx([["Id.Value", "Name.Value"], ["", "B"]]));
            await BulkFileTransfer.GetListFileAsync(design, CreateIO(design), new SearchCondition { ModuleName = "Item" });

            Assert.That(await CountAsync(), Is.EqualTo(2));
            Assert.That(_audit.Event.Targets, Is.Empty);
        }
    }
}
