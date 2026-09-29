using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// EditHistoryRecorder と埋め込みモジュール (ModuleField): 本体の従属レコード宣言 (ModuleFieldDesign) により、
    /// 子レコードが全列 (+ NULL 埋め) で版に入る。参照の位置に子レコード 1 行の一覧として入る。記録は内部読みなので子モジュールを読めないユーザーの保存でも入り、返すときに落ちる。
    /// </summary>
    public class EditHistoryModuleFieldDbTest : IAuthenticationContext
    {
        const string Ds = EditHistoryTestDesigns.Ds;
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("7");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_module_field_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, amount REAL, secret TEXT, customer_id INTEGER, is_deleted INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE order_items (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER, name TEXT, qty REAL, supplier_id INTEGER, is_deleted INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE customers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, note TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT, user_id TEXT, date_time TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, name) VALUES (7, '石川')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO customers (id, name, note) VALUES (5, 'A社', NULL)", new());
            _design = EditHistoryTestDesigns.Create(withCustomer: true);
            _errors.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        //テンプレートの CustomizedModuleDataIO と同じ結線 (インターセプタを 1 つ登録)
        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<List<ModuleData>> SnapshotsAsync()
            => (await _db.QueryAsync(Ds, "SELECT snapshot FROM edit_histories ORDER BY id", new()))
                .Select(e => EditHistorySnapshot.Deserialize(e["snapshot"].ToString())!).ToList();

        static ModuleData OrderData(string id, string title, string customerId)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            if (customerId.Length != 0) data.Fields["Customer"] = new ModuleFieldData { Id = customerId };
            return data;
        }

        static ModuleData CustomerData(string id, string name)
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            return data;
        }

        async Task CreateOrderAsync(string customerId = "5")
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempId, Add = [OrderData(tempId, "受注A", customerId)],
            }]));
        }

        [Test]
        public async Task 子レコードは埋め込みのデータとして全列で入り_NULLの列も空として残る()
        {
            await CreateOrderAsync();

            var embedded = (ModuleFieldData)(await SnapshotsAsync()).Single().Fields["Customer"];
            Assert.That(embedded.Id, Is.EqualTo("5"), "参照 (親の列の値) もそのまま持つ");
            var customer = embedded.Data;
            Assert.That(EditHistorySnapshot.GetId(customer), Is.EqualTo("5"));
            Assert.That(((TextFieldData)customer.Fields["Name"]).Value, Is.EqualTo("A社"));
            Assert.That(customer.Fields.ContainsKey("Note"), Is.True, "詳細レイアウトに無い列も読む");
            Assert.That(((TextFieldData)customer.Fields["Note"]).Value, Is.Null, "NULL の列は null 値として残す (復元で空に戻せる)");
            Assert.That(_errors, Is.Empty);
        }

        async Task<string> DeleteOrderAndRestoreAsync(Func<Task>? betweenDeleteAndRestore = null)
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(0));
            if (betweenDeleteAndRestore != null) await betweenDeleteAndRestore();
            var deleteRow = (await _db.QueryAsync(Ds, "SELECT id FROM edit_histories WHERE change_type = 'Delete'", new())).Single();
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            return results[0].DestinationId;
        }

        [Test]
        public async Task 物理削除した親の復活で子への参照が戻り_今もある子は作り直さない()
        {
            await CreateOrderAsync();
            var newId = await DeleteOrderAndRestoreAsync();

            var order = (await _db.QueryAsync(Ds, "SELECT id, customer_id FROM orders", new())).Single();
            Assert.That(order["id"]!.ToString(), Is.EqualTo(newId));
            Assert.That(order["customer_id"]!.ToString(), Is.EqualTo("5"), "埋め込みの子 (親の削除では消えない) への参照が戻る");
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM customers", new())).Single()["c"], Is.EqualTo(1), "子は作り直さない");
            var restored = (await SnapshotsAsync()).Last();
            Assert.That(EditHistorySnapshot.GetId(restored.GetOwnedRows("Customer")!.Single()), Is.EqualTo("5"));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 物理削除した親の復活で子も消えていれば子を作り直して参照する()
        {
            await CreateOrderAsync();
            //親の削除の後 (削除の版には子が入っている) に子も消えた
            var newId = await DeleteOrderAndRestoreAsync(() => _db.ExecuteAsync(Ds, "DELETE FROM customers", new()));

            var customer = (await _db.QueryAsync(Ds, "SELECT id, name FROM customers", new())).Single();
            Assert.That(customer["name"], Is.EqualTo("A社"), "版の内容で作り直す");
            var order = (await _db.QueryAsync(Ds, "SELECT customer_id FROM orders", new())).Single();
            Assert.That(order["customer_id"]!.ToString(), Is.EqualTo(customer["id"]!.ToString()), "親の参照は作り直した子の Id");
            Assert.That(newId, Is.Not.EqualTo("1"));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 子を参照していない親は子無しで記録される()
        {
            await CreateOrderAsync(customerId: string.Empty);
            Assert.That((await SnapshotsAsync()).Single().GetOwnedRows("Customer"), Is.Empty);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 親の保存に乗った子の変更が親の版になり_差分は行1の項目で出る()
        {
            await CreateOrderAsync();
            //画面の保存と同じ形: ModuleField.GetSubmitData が子の Update を親の送信に乗せる
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注A", "5"), CustomerData("5", "B社")],
            }]));

            var snapshots = await SnapshotsAsync();
            Assert.That(snapshots.Count, Is.EqualTo(2));
            Assert.That(((TextFieldData)snapshots[1].GetOwnedRows("Customer")!.Single().Fields["Name"]).Value, Is.EqualTo("B社"));

            var change = EditHistoryDiff.Compute(_design, _design.Modules.Find("Order")!, snapshots[0], snapshots[1], _ => true).Single();
            Assert.That((change.FieldName, change.IsList, change.ChangedCount), Is.EqualTo(("Customer", true, 1)));
            Assert.That(change.Rows.Single().Changes.Select(e => (e.DisplayName, e.Before, e.After)), Is.EqualTo(new[] { ("顧客名", "A社", "B社") }));
        }

        [Test]
        public async Task 子モジュールを読めないユーザーの保存でも子の中身を記録し_返すときに参照だけに落とす()
        {
            _design.Modules.Find("Customer")!.UserReadCondition = new ModuleMatchCondition
            {
                ModuleName = "AppUser",
                Condition = new FieldValueMatchCondition
                {
                    SearchTargetVariable = "Id.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("__nobody__"),
                },
            };
            await CreateOrderAsync();

            //DB の記録 (生) には子の中身も入る (記録は内部読み)
            var raw = (await SnapshotsAsync()).Single().Fields["Customer"];
            Assert.That(((TextFieldData)((ModuleFieldData)raw).Data.Fields["Name"]).Value, Is.EqualTo("A社"), "操作ユーザーの権限に関係なく子レコードが入る");
            Assert.That(_errors, Is.Empty);

            //履歴モジュールを読むと、子モジュールを読めない人には参照 (親の列) だけ残る (通常の読み出しと同じ形)
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "EditHistory" }, 0);
            var served = EditHistorySnapshot.Deserialize(((TextFieldData)page.Items.Single().Fields["Snapshot"]).Value)!;
            var customer = served.Fields["Customer"];
            Assert.That(customer, Is.InstanceOf<ModuleFieldData>());
            Assert.That(((ModuleFieldData)customer).Id, Is.EqualTo("5"));
            Assert.That(((ModuleFieldData)customer).Data.Fields, Is.Empty);
            Assert.That(served.GetOwnedRows("Customer"), Is.Null, "内容を持たない参照だけ");
        }
    }
}
