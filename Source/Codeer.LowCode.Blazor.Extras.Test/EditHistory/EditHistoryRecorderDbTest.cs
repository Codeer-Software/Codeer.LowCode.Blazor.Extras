using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// EditHistoryRecorder: 実 DB (SQLite) で、保存 (作成・更新・削除) ごとに履歴モジュールへ
    /// レコード全体 (明細込み) のスナップショットが 1 行書かれること。
    /// </summary>
    public class EditHistoryRecorderDbTest : IAuthenticationContext
    {
        const string Ds = EditHistoryTestDesigns.Ds;
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("7");

        /// <summary>テンプレートの CustomizedModuleDataIO と同じ結線 (SubmitAsync を Recorder で包む)。</summary>
        sealed class HistoryModuleDataIO : ModuleDataIO
        {
            readonly EditHistoryRecorder _recorder;

            public HistoryModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, List<string> errors)
                : base(design, auth, db, files)
                => _recorder = new EditHistoryRecorder(design, this, AddSystemRecordAsync, errors.Add);

            public override Task<List<ModuleSubmitResult>> SubmitAsync(Guid transactionId, List<ModuleSubmitData> transactionData)
                => _recorder.SubmitAsync(transactionData, () => base.SubmitAsync(transactionId, transactionData));

            Task<string> AddSystemRecordAsync(ModuleData data) => AddAsync(Guid.NewGuid(), Guid.NewGuid(), data);
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, amount REAL, secret TEXT, is_deleted INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE order_items (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER, name TEXT, qty REAL, is_deleted INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT, user_id TEXT, date_time TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, name) VALUES (7, '石川')", new());
            _design = EditHistoryTestDesigns.Create();
            _errors.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new HistoryModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), _errors);

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<List<Dictionary<string, object>>> HistoriesAsync()
            => (await _db.QueryAsync(Ds, "SELECT module_name, data_id, change_type, snapshot, user_id, date_time FROM edit_histories ORDER BY id", new()))
                .Select(e => e.ToDictionary(x => x.Key, x => x.Value)).ToList();

        static ModuleData OrderData(string id, string title, decimal amount)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Amount"] = new NumberFieldData { Value = amount };
            return data;
        }

        static ModuleData ItemData(string? id, string orderId, string name, decimal qty)
        {
            var data = new ModuleData { Name = "OrderItem" };
            if (id != null) data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Order"] = new LinkFieldData { Value = orderId };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Qty"] = new NumberFieldData { Value = qty };
            return data;
        }

        static ModuleData Snapshot(Dictionary<string, object> history)
            => EditHistorySnapshot.Deserialize(history["snapshot"].ToString())!;

        static List<ModuleData> Items(ModuleData snapshot) => ((ListFieldData)snapshot.Fields["Items"]).Children;

        //受注 1 件 + 明細 2 行を 1 回の保存で作る (明細の FK は親の仮 Id 参照)
        async Task CreateOrderAsync()
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempId,
                Add = [OrderData(tempId, "受注A", 1000), ItemData(null, tempId, "品X", 1), ItemData(null, tempId, "品Y", 2)],
            }]));
        }

        [Test]
        public async Task 作成で保存後の内容_明細込み_が1行記録される()
        {
            await CreateOrderAsync();

            var histories = await HistoriesAsync();
            Assert.That(histories.Count, Is.EqualTo(1));
            var h = histories[0];
            Assert.That((h["module_name"], h["data_id"], h["change_type"], h["user_id"]), Is.EqualTo(("Order", "1", "Add", "7")));
            Assert.That(h["date_time"], Is.Not.Null.And.Not.EqualTo(DBNull.Value));

            var snapshot = Snapshot(h);
            Assert.That(snapshot.Name, Is.EqualTo("Order"));
            Assert.That(((TextFieldData)snapshot.Fields["Title"]).Value, Is.EqualTo("受注A"));
            Assert.That(((NumberFieldData)snapshot.Fields["Amount"]).Value, Is.EqualTo(1000));
            var items = Items(snapshot);
            Assert.That(items.Select(e => ((TextFieldData)e.Fields["Name"]).Value), Is.EquivalentTo(new[] { "品X", "品Y" }));
            Assert.That(items.All(e => !string.IsNullOrEmpty(EditHistorySnapshot.GetId(e))), Is.True, "明細行の Id も入る (復元時の突き合わせに使う)");
            Assert.That(snapshot.Fields.ContainsKey("Related"), Is.False, "従属でない一覧はスナップショットに含めない");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 更新_明細の更新追加削除込み_で保存後の内容が記録される()
        {
            await CreateOrderAsync();

            var update = OrderData("1", "受注A改", 1500);
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1",
                Update = [update, ItemData("1", "1", "品X", 9)],
                Add = [ItemData(null, "1", "品Z", 3)],
                Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));

            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update" }));
            var snapshot = Snapshot(histories[1]);
            Assert.That(((TextFieldData)snapshot.Fields["Title"]).Value, Is.EqualTo("受注A改"));
            var items = Items(snapshot);
            Assert.That(items.Select(e => (((TextFieldData)e.Fields["Name"]).Value, ((NumberFieldData)e.Fields["Qty"]).Value)),
                Is.EquivalentTo(new[] { ("品X", (decimal?)9), ("品Z", (decimal?)3) }));
        }

        [Test]
        public async Task 明細だけの更新でも親の履歴が記録される()
        {
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Update = [ItemData("1", "1", "品X", 4)],
            }]));
            var histories = await HistoriesAsync();
            Assert.That(histories.Count, Is.EqualTo(2));
            Assert.That((histories[1]["data_id"], histories[1]["change_type"]), Is.EqualTo(("1", "Update")));
            var itemX = Items(Snapshot(histories[1])).Single(e => ((TextFieldData)e.Fields["Name"]).Value == "品X");
            Assert.That(((NumberFieldData)itemX.Fields["Qty"]).Value, Is.EqualTo(4));
        }

        [Test]
        public async Task 削除で削除前の内容が記録される()
        {
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));

            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(0));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM order_items", new())).Single()["c"], Is.EqualTo(0), "従属の明細も消える");
            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Delete" }));
            var snapshot = Snapshot(histories[1]);
            Assert.That(((TextFieldData)snapshot.Fields["Title"]).Value, Is.EqualTo("受注A"));
            Assert.That(Items(snapshot).Count, Is.EqualTo(2), "削除前の明細が入る");
        }

        [Test]
        public async Task 履歴フィールドの無いモジュールの保存は記録しない()
        {
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "1", Update = [ItemData("1", "1", "品X", 4)],
            }]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1));
        }

        [Test]
        public async Task 履歴モジュールの設定不備は保存を失敗にする()
        {
            _design = EditHistoryTestDesigns.Create(historyModuleName: "Nothing");
            var tempId = "@temporary:" + Guid.NewGuid();
            var results = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempId, Add = [OrderData(tempId, "受注A", 1000)],
            }]);
            Assert.That(results.All(e => e.ExceptionMessage.Contains("Nothing")), Is.True);
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(0), "保存されない");
            Assert.That(_errors.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ExecuteSqlのStandalone送信はレコードが変わったときだけ記録する()
        {
            var sql = new Repository.Design.ExecuteSqlFieldDesign { Name = "Sql", Timing = Repository.Design.ExecuteSqlTiming.Standalone };
            sql.ExecuteSqlSetting.SqlText = "UPDATE orders SET title = 'SQLで更新' WHERE id = 1";
            _design.Modules.Find("Order")!.Fields.Add(sql);
            await CreateOrderAsync();

            //Add / Update / Delete 無し = Standalone の SQL だけが走る
            //CurrentEditingData はクライアントが送る形 (デザイン上の一覧フィールドのデータを含む)
            static ModuleSubmitData Standalone()
            {
                var editing = new ModuleData { Name = "Order" };
                editing.Fields["Id"] = new IdFieldData { Value = "1" };
                editing.Fields["Items"] = new ListFieldData();
                editing.Fields["Related"] = new ListFieldData();
                return new() { ModuleName = "Order", Id = "1", CurrentEditingData = editing };
            }
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([Standalone()]));
            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(((TextFieldData)Snapshot(histories[1]).Fields["Title"]).Value, Is.EqualTo("SQLで更新"));

            //同じ SQL をもう一度 = レコードは変わらない → 版は増えない
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([Standalone()]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(2));
        }

        [Test]
        public async Task 論理削除したレコードは削除の取り消しで明細ごと同じIdで復活し_復活の版が記録される()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders WHERE is_deleted = 1", new())).Single()["c"], Is.EqualTo(1));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM order_items WHERE is_deleted = 1", new())).Single()["c"], Is.EqualTo(2), "明細も論理削除される");

            //復活ボタンが送る形: Add / Update 無し、ExtendedData に親と明細の Id
            var undelete = new EditHistoryUndeleteData();
            undelete.Targets.Add(new EditHistoryUndeleteTarget { ModuleName = "Order", Id = "1" });
            undelete.Targets.Add(new EditHistoryUndeleteTarget { ModuleName = "OrderItem", Id = "1" });
            undelete.Targets.Add(new EditHistoryUndeleteTarget { ModuleName = "OrderItem", Id = "2" });
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(undelete);
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([submit]));

            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders WHERE is_deleted = 1", new())).Single()["c"], Is.EqualTo(0));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM order_items WHERE is_deleted = 1", new())).Single()["c"], Is.EqualTo(0));
            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Delete", "Restore" }));
            var snapshot = Snapshot(histories[2]);
            Assert.That(EditHistorySnapshot.GetId(snapshot), Is.EqualTo("1"), "Id は変わらない");
            Assert.That(Items(snapshot).Count, Is.EqualTo(2), "明細も戻っている");
        }

        [Test]
        public async Task 論理削除した明細行は復元の保存で同じIdのまま戻る()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            //明細 2 を消して保存
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            Assert.That(Items(Snapshot((await HistoriesAsync())[1])).Count, Is.EqualTo(1));

            //「この版に戻す」→ 保存が送る形: 復活した行の Update + ExtendedData の取り消し
            var undelete = new EditHistoryUndeleteData();
            undelete.Targets.Add(new EditHistoryUndeleteTarget { ModuleName = "OrderItem", Id = "2" });
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [ItemData("2", "1", "品Y", 7)] };
            submit.ExtendedData.Add(undelete);
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([submit]));

            var items = Items(Snapshot((await HistoriesAsync())[2]));
            Assert.That(items.Select(e => (EditHistorySnapshot.GetId(e), ((NumberFieldData)e.Fields["Qty"]).Value)),
                Is.EquivalentTo(new[] { ("1", (decimal?)1), ("2", (decimal?)7) }), "Id 2 のまま戻り、値も更新されている");
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update", "Update" }));
        }

        [Test]
        public async Task 物理削除のモジュールへの削除の取り消しは保存を失敗にする()
        {
            await CreateOrderAsync();
            var undelete = new EditHistoryUndeleteData();
            undelete.Targets.Add(new EditHistoryUndeleteTarget { ModuleName = "Order", Id = "1" });
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "X", 1)] };
            submit.ExtendedData.Add(undelete);
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That((await _db.QueryAsync(Ds, "SELECT title FROM orders WHERE id = 1", new())).Single()["title"], Is.EqualTo("受注A"), "ロールバック");
        }

        [Test]
        public async Task 任意役割が空なら記録しない()
        {
            var contract = _design.Modules.Find("EditHistory")!.Fields.OfType<Extras.Designs.EditHistoryContractFieldDesign>().Single();
            contract.UserId = string.Empty;
            contract.DateTime = string.Empty;
            await CreateOrderAsync();
            var h = (await HistoriesAsync()).Single();
            Assert.That(h["user_id"], Is.Null.Or.EqualTo(DBNull.Value));
            Assert.That(h["date_time"], Is.Null.Or.EqualTo(DBNull.Value));
        }
    }
}
