using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    /// <summary>
    /// 一覧の完全洗い替え (ListReplaceMode.All) の保存の監査。実 DB (SQLite) で、本番と同じく
    /// 保存の合流点のインターセプタ (<see cref="AuditIOInterceptor"/>) と今のリクエストの監査レコード (<see cref="AuditContext.Current"/>) を通す。
    /// 洗い替えで消える行は本体が保存の入口で送信の Delete に直すので、行ごとの Delete (Id つき) として対象に残る。
    /// </summary>
    public class ListReplaceAllAuditTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = [];
        DbAccessor _db = null!;
        //保存に使う接続。本番と同じくリクエストごとの新しい DbAccessor
        DbAccessor _ioDb = null!;
        AuditContext _audit = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        //受注 (Order) の明細 (OrderItem) の一覧を完全洗い替えにする
        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var item = new ModuleDesign { Name = "OrderItem", DataSourceName = Ds, DbTable = "order_items" };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            item.Fields.Add(new IdFieldDesign { Name = "OrderId", DbColumn = "order_id", IsManualInput = false });
            item.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            item.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(item);

            var order = new ModuleDesign { Name = "Order", DataSourceName = Ds, DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            order.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            order.Fields.Add(new ListFieldDesign
            {
                Name = "Items",
                ReplaceMode = ListReplaceMode.All,
                CanCreate = true,
                CanUpdate = true,
                CanDelete = true,
                SearchCondition = new SearchCondition("OrderItem")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "OrderId.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                },
            });
            order.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(order);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"audit_replace_all_test_{Guid.NewGuid():N}.db");
            _dataSources = [new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }];
            _db = new DbAccessor(_dataSources);
            _ioDb = new DbAccessor(_dataSources);
            await _db.ExecuteAsync(Ds, "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE order_items (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (id, title) VALUES (1, '受注A'), (2, '受注B')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (id, order_id, name) VALUES (1, 1, '品X'), (2, 1, '品Y'), (3, 2, '品Z')", new());
            _audit = new AuditContext();
        }

        [TearDown]
        public async Task TearDown()
        {
            AuditContext.Current = null;
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

        [Test]
        public async Task ReplaceAllRecordsEachRemovedRowAsDeleteWithItsId()
        {
            //リクエストの間だけ見える監査レコード (本番はミドルウェアが入れる)
            AuditContext.Current = _audit;
            var design = CreateDesign();

            //受注A の画面から明細を 1 行 (品W) に洗い替える
            var tempId = IdFieldData.NewId().Value!;
            var row = new ModuleData { Name = "OrderItem" };
            row.Fields["Id"] = new IdFieldData { Value = tempId };
            row.Fields["OrderId"] = new IdFieldData { Value = "1" };
            row.Fields["Name"] = new TextFieldData { Value = "品W" };
            //受注A のデータに一覧の洗い替えの印を載せる (一覧だけなので受注A 自身は更新されない)
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = "1" };
            order.Fields["Items"] = new ListFieldData { IsReplaceAll = true };
            var results = await CreateIO(design).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [order], Add = [row] }]);

            Assert.That(results.Select(e => e.ExceptionMessage).Where(e => !string.IsNullOrEmpty(e)), Is.Empty);
            var rows = await _db.QueryAsync(Ds, "SELECT id, order_id, name FROM order_items ORDER BY id", new());
            Assert.That(rows.Select(e => (e["order_id"]!.ToString(), e["name"]!.ToString())), Is.EqualTo(new[] { ("2", "品Z"), ("1", "品W") }), "受注B の行は残る");
            var added = rows.Single(e => e["name"]!.ToString() == "品W")["id"]!.ToString();

            Assert.That(_audit.Event.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(_audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)), Is.EquivalentTo(new[]
            {
                ("OrderItem", (string?)added, "Add"),
                ("OrderItem", "1", "Delete"),
                ("OrderItem", "2", "Delete"),
            }), "消えた行は行ごとの Delete (Id つき)。別の親の行 (3) は対象に無い");
            Assert.That(_audit.ComposeDetail(), Is.EqualTo("Add=1; Update=0; Delete=2"));
        }
    }
}
