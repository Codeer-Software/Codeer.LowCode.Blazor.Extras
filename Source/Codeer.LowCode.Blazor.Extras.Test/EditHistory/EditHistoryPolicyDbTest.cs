using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// EditHistoryRecorder と従属レコードの扱い:
    /// 除外 (ExcludedOwnedRecords) = 親の版に読まない。
    /// 行ごと (IndividuallyRecordedOwnedRecords) = 親の版に読まず、親の送信に乗った行を行のモジュールの履歴に 1 行 1 版で記録する
    /// (作成は採番 Id・削除は削除前の内容)。行だけの送信は親の版にならない。
    /// </summary>
    public class EditHistoryPolicyDbTest : IAuthenticationContext
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
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_policy_test_{Guid.NewGuid():N}.db");
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
            SqliteTestDb.Delete(_dbFile);
        }

        //テンプレートの CustomizedModuleDataIO と同じ結線 (インターセプタを 1 つ登録)
        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }
        EditHistoryFieldDesign History => _design.Modules.Find("Order")!.Fields.OfType<EditHistoryFieldDesign>().Single();

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<List<(string Module, string DataId, string ChangeType, ModuleData Snapshot)>> HistoriesAsync()
            => (await _db.QueryAsync(Ds, "SELECT module_name, data_id, change_type, snapshot FROM edit_histories ORDER BY id", new()))
                .Select(e => (e["module_name"].ToString()!, e["data_id"].ToString()!, e["change_type"].ToString()!, EditHistorySnapshot.Deserialize(e["snapshot"].ToString())!)).ToList();

        static ModuleData OrderData(string id, string title)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            return data;
        }

        static ModuleData ItemData(string? id, string orderId, string name)
        {
            var data = new ModuleData { Name = "OrderItem" };
            if (id != null) data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Order"] = new LinkFieldData { Value = orderId };
            data.Fields["Name"] = new TextFieldData { Value = name };
            return data;
        }

        static string Name(ModuleData data) => ((TextFieldData)data.Fields["Name"]).Value!;

        //受注 1 件 + 明細 2 行 (画面と同じく仮 Id 付き) を 1 回の保存で作る
        async Task CreateOrderAsync()
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempId,
                Add = [OrderData(tempId, "受注A"), ItemData(IdFieldData.NewId().Value, tempId, "品X"), ItemData(IdFieldData.NewId().Value, tempId, "品Y")],
            }]));
        }

        [Test]
        public async Task 除外した一覧は親の版に入らず_その行だけの保存は親の版にならない()
        {
            History.ExcludedOwnedRecords.Add("Items");
            await CreateOrderAsync();
            var h = (await HistoriesAsync()).Single();
            Assert.That((h.Module, h.ChangeType), Is.EqualTo(("Order", "Add")));
            Assert.That(h.Snapshot.Fields.ContainsKey("Items"), Is.False);

            //除外した明細だけの変更: 親には変更が無いので版は増えない (「変更なし」の版を作らない)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Update = [ItemData("1", "1", "品X改")], Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1));

            //親も変える保存は版になる (明細は入らない)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注A改")],
            }]));
            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(histories[1].Snapshot.Fields.ContainsKey("Items"), Is.False);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 行ごとの一覧は親の版に入らず_行が行のモジュールの履歴に1行1版で残る()
        {
            _design.Modules.Find("OrderItem")!.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            History.IndividuallyRecordedOwnedRecords.Add("Items");

            //作成: 親の版 (明細なし) + 明細 2 行それぞれの作成の版 (採番 Id で)
            await CreateOrderAsync();
            var histories = await HistoriesAsync();
            var parent = histories.Single(e => e.Module == "Order");
            Assert.That((parent.DataId, parent.ChangeType), Is.EqualTo(("1", "Add")));
            Assert.That(parent.Snapshot.Fields.ContainsKey("Items"), Is.False, "行ごとの一覧は親の版に入らない");
            var rows = histories.Where(e => e.Module == "OrderItem").ToList();
            Assert.That(rows.Select(e => (e.DataId, e.ChangeType, Name(e.Snapshot))).OrderBy(e => e.DataId), Is.EqualTo(new[] { ("1", "Add", "品X"), ("2", "Add", "品Y") }));

            //明細だけの変更 (品X 改名・品Y 削除・品Z 追加): 親の版は増えず、行の版だけ増える。削除は削除前の内容
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1",
                Add = [ItemData(IdFieldData.NewId().Value, "1", "品Z")],
                Update = [ItemData("1", "1", "品X改")],
                Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            histories = await HistoriesAsync();
            Assert.That(histories.Count(e => e.Module == "Order"), Is.EqualTo(1), "親には変更が無いので親の版は増えない");
            var later = histories.Where(e => e.Module == "OrderItem").Skip(2).Select(e => (e.DataId, e.ChangeType, Name(e.Snapshot))).OrderBy(e => e).ToList();
            Assert.That(later, Is.EqualTo(new[] { ("1", "Update", "品X改"), ("2", "Delete", "品Y"), ("3", "Add", "品Z") }));

            //親も変える保存: 親の版 (明細なし) と行の版の両方
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注A改"), ItemData("3", "1", "品Z改")],
            }]));
            histories = await HistoriesAsync();
            Assert.That(histories.Where(e => e.Module == "Order").Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(histories.Last().Module == "OrderItem" && histories.Last().ChangeType == "Update" && Name(histories.Last().Snapshot) == "品Z改", Is.True);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 行のモジュールにEditHistoryFieldが無ければ行の版は作らずログに残す()
        {
            History.IndividuallyRecordedOwnedRecords.Add("Items");
            await CreateOrderAsync();
            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e.Module), Is.EqualTo(new[] { "Order" }));
            Assert.That(histories[0].Snapshot.Fields.ContainsKey("Items"), Is.False);
            Assert.That(_errors.Count, Is.EqualTo(1));
            Assert.That(_errors[0], Does.Contain("OrderItem"));
        }
    }
}
