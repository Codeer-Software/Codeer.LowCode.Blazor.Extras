using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Tag;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 親の版に含める従属レコードの行を、親の画面以外 (行のモジュール自身の送信・取込・他のモジュールの送信) で保存したときも、
    /// その行を含む親の 1 版 (Update・保存後の親全体のスナップショット) になる。
    /// </summary>
    public class EditHistoryOwnerVersionDbTest : IAuthenticationContext
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
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_owner_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, amount REAL, secret TEXT, is_deleted INTEGER, customer_id INTEGER, item_id INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE order_items (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER, quote_id INTEGER, name TEXT, qty REAL, is_deleted INTEGER, supplier_id INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE order_item_details (id INTEGER PRIMARY KEY AUTOINCREMENT, item_id INTEGER, memo TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE customers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, note TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE customer_contacts (id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE quotes (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, customer_id INTEGER)", new());
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

        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        static void AssertFailed(List<ModuleSubmitResult> results)
            => Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "保存は失敗する");

        async Task<List<(string Module, string DataId, string ChangeType, ModuleData Snapshot)>> HistoriesAsync()
            => (await _db.QueryAsync(Ds, "SELECT module_name, data_id, change_type, snapshot FROM edit_histories ORDER BY id", new()))
                .Select(e => (e["module_name"].ToString()!, e["data_id"].ToString()!, e["change_type"].ToString()!, EditHistorySnapshot.Deserialize(e["snapshot"].ToString())!)).ToList();

        async Task<object?> ScalarAsync(string sql) => (await _db.QueryAsync(Ds, sql, new())).Single().Values.First();

        static ModuleData OrderData(string id, string title)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
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

        static string? Text(ModuleData data, string field) => ((TextFieldData)data.Fields[field]).Value;
        static decimal? Number(ModuleData data, string field) => ((NumberFieldData)data.Fields[field]).Value;
        static List<ModuleData> Rows(ModuleData data, string field) => ((ListFieldData)data.Fields[field]).Children;
        static List<ModuleData> Items(ModuleData snapshot) => Rows(snapshot, "Items");

        //受注 (Title) + 明細 (品X 1 / 品Y 2) を親の画面と同じ形で作る。受注 Id は 1 から順に振られる
        async Task CreateOrderAsync(string title = "受注A")
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempId,
                Add = [OrderData(tempId, title), ItemData(IdFieldData.NewId().Value, tempId, "品X", 1), ItemData(IdFieldData.NewId().Value, tempId, "品Y", 2)],
            }]));
        }

        //行のモジュール (OrderItem) 自身の送信で行を更新する
        Task<List<ModuleSubmitResult>> UpdateItemAsync(ModuleData item)
            => CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = ((IdFieldData)item.Fields["Id"]).Value!, Update = [item] }]);

        //見積 (Quote): withItems なら OrderItem を指す 2 つ目の従属一覧 (OrderItem.Quote で親を指す)、withCustomer なら埋め込みの Customer
        static void AddQuote(DesignData d, bool withItems, bool withCustomer, bool withHistory = true)
        {
            var quote = new ModuleDesign { Name = "Quote", DataSourceName = Ds, DbTable = "quotes" };
            quote.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            quote.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            if (withItems)
            {
                quote.Fields.Add(new ListFieldDesign
                {
                    Name = "Items",
                    DeleteTogether = true,
                    SearchCondition = new SearchCondition("OrderItem")
                    {
                        Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Quote.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                    },
                });
                d.Modules.Find("OrderItem")!.Fields.Add(new LinkFieldDesign { Name = "Quote", SearchCondition = new SearchCondition("Quote"), DbColumn = "quote_id" });
            }
            if (withCustomer)
                quote.Fields.Add(new ModuleFieldDesign { Name = "Customer", DbColumn = "customer_id", ModuleName = "Customer", LayoutName = "" });
            if (withHistory) quote.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            quote.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(quote);
        }

        static EditHistoryFieldDesign HistoryOf(DesignData d, string module) => d.Modules.Find(module)!.Fields.OfType<EditHistoryFieldDesign>().Single();

        static List<DesignCheckInfo> Check(DesignData d, string module)
            => HistoryOf(d, module).CheckDesign(new DesignCheckContext(module, d, Utilities.CreateDataSource()));

        static readonly string MultipleOwners = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), 12);

        // ===== 1 〜 6: 行のモジュールの送信が親の版になる =====

        [Test]
        public async Task 行のモジュールの送信で行を更新すると親の更新の版が1つ増え新しい値が入る()
        {
            await CreateOrderAsync();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            var items = Items(h[1].Snapshot);
            Assert.That(items.Select(e => (Text(e, "Name"), Number(e, "Qty"))), Is.EquivalentTo(new[] { ("品X改", (decimal?)9), ("品Y", (decimal?)2) }));
            Assert.That(Text(h[1].Snapshot, "Title"), Is.EqualTo("受注A"), "保存後の親全体のスナップショット");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 行のモジュールの送信で親へのリンク付きの行を追加すると親の版になる()
        {
            await CreateOrderAsync();
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)],
            }]));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X", "品Y", "品Z" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 行のモジュールの送信で行を削除すると親の版になりスナップショットから行が消える()
        {
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EqualTo(new[] { "品X" }));
        }

        [Test]
        public async Task 行を別の親へ付け替える更新は元の親と新しい親の両方の版になる()
        {
            await CreateOrderAsync("受注A");
            await CreateOrderAsync("受注B");
            //受注A の 品X (Id 1) を 受注B (Id 2) へ
            AssertNoError(await UpdateItemAsync(ItemData("1", "2", "品X", 1)));

            var h = await HistoriesAsync();
            Assert.That(h.Take(2).Select(e => (e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("1", "Add"), ("2", "Add") }));
            var later = h.Skip(2).ToList();
            Assert.That(later.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EquivalentTo(new[] { ("Order", "1", "Update"), ("Order", "2", "Update") }));
            var a = later.Single(e => e.DataId == "1").Snapshot;
            var b = later.Single(e => e.DataId == "2").Snapshot;
            Assert.That(Items(a).Select(e => Text(e, "Name")), Is.EqualTo(new[] { "品Y" }), "元の親からは消える");
            Assert.That(Items(b).Select(e => EditHistorySnapshot.GetId(e)), Is.EquivalentTo(new[] { "1", "3", "4" }), "新しい親に入る");
        }

        [Test]
        public async Task 一回の送信で同じ親の行を複数変更しても親の版は1つ()
        {
            await CreateOrderAsync();
            //1 つの送信に 2 行
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "1", Update = [ItemData("1", "1", "品X改", 1), ItemData("2", "1", "品Y改", 2)],
            }]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(2));

            //1 つのトランザクションに送信 2 つ (更新 + 追加 + 削除)
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([
                new ModuleSubmitData { ModuleName = "OrderItem", Id = "1", Update = [ItemData("1", "1", "品X改2", 1)] },
                new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] },
                new ModuleSubmitData { ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }] },
            ]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update", "Update" }));
            Assert.That(Items(h[2].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改2", "品Z" }));
        }

        [Test]
        public async Task 親の画面からの保存は今までどおり1版だけ()
        {
            await CreateOrderAsync();
            //明細だけ (親の送信に行が乗る)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Update = [ItemData("1", "1", "品X改", 4)],
            }]));
            Assert.That((await HistoriesAsync()).Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));

            //親 + 明細の更新・追加・削除
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1",
                Update = [OrderData("1", "受注A改"), ItemData("1", "1", "品X改2", 5)],
                Add = [ItemData(null, "1", "品Z", 3)],
                Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update", "Update" }), "二重にならない");
            Assert.That(Text(h[2].Snapshot, "Title"), Is.EqualTo("受注A改"));
            Assert.That(Items(h[2].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改2", "品Z" }));

            //親の画面からの新規作成も 1 版 (Add) だけ
            await CreateOrderAsync("受注B");
            Assert.That((await HistoriesAsync()).Where(e => e.DataId == "2").Select(e => e.ChangeType), Is.EqualTo(new[] { "Add" }));
            Assert.That(_errors, Is.Empty);
        }

        // ===== 7 〜 8: 親の版に入らない行 =====

        [Test]
        public async Task 親の一覧の条件に合わない行の保存は親の版にならない()
        {
            //明細の一覧は 数量 1 以上の行だけ
            var items = _design.Modules.Find("Order")!.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items");
            items.SearchCondition.Condition = MultiMatchCondition.And(
                new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                new FieldValueMatchCondition { SearchTargetVariable = "Qty.Value", Comparison = MatchComparison.GreaterThanOrEqual, Value = MultiTypeValue.Create(1m) });
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '対象外', 0)", new());

            //条件に合わない行 (3) の更新
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "対象外改", 0)));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1), "親の一覧に入らない行は親の一部ではない");

            //条件に合うように変えると親の版になる (保存後に一覧に入る)
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "対象内", 5)));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X", "品Y", "対象内" }));
        }

        [Test]
        public async Task 除外した従属レコードの行の保存は親の版にならない()
        {
            HistoryOf(_design, "Order").ExcludedOwnedRecords.Add("Items");
            await CreateOrderAsync();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.ChangeType)), Is.EqualTo(new[] { ("Order", "Add") }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 行ごとの従属レコードの行の保存は親の版にならず行のモジュールの履歴に残る()
        {
            _design.Modules.Find("OrderItem")!.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            HistoryOf(_design, "Order").IndividuallyRecordedOwnedRecords.Add("Items");
            await CreateOrderAsync();
            var before = await HistoriesAsync();
            Assert.That(before.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EquivalentTo(new[] { ("Order", "1", "Add"), ("OrderItem", "1", "Add"), ("OrderItem", "2", "Add") }));

            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var h = await HistoriesAsync();
            Assert.That(h.Skip(3).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("OrderItem", "1", "Update") }), "親の版は増えず、行の版だけ");
            Assert.That(Text(h[3].Snapshot, "Name"), Is.EqualTo("品X改"));
            Assert.That(_errors, Is.Empty);
        }

        // ===== 9 〜 10: 孫・自己参照 =====

        [Test]
        public async Task 孫の行を孫のモジュールの送信で保存するとルートの親の版になる()
        {
            _design = EditHistoryTestDesigns.Create(withDetails: true);
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO order_item_details (item_id, memo) VALUES (1, 'メモ1')", new());

            var detail = new ModuleData { Name = "OrderItemDetail" };
            detail.Fields["Id"] = new IdFieldData { Value = "1" };
            detail.Fields["Item"] = new LinkFieldData { Value = "1" };
            detail.Fields["Memo"] = new TextFieldData { Value = "メモ改" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItemDetail", Id = "1", Update = [detail] }]));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            var itemX = Items(h[1].Snapshot).Single(e => EditHistorySnapshot.GetId(e) == "1");
            Assert.That(Rows(itemX, "Details").Select(e => Text(e, "Memo")), Is.EqualTo(new[] { "メモ改" }));

            //孫の追加・削除も同じ
            var tempId = "@temporary:" + Guid.NewGuid();
            var added = new ModuleData { Name = "OrderItemDetail" };
            added.Fields["Id"] = new IdFieldData { Value = tempId };
            added.Fields["Item"] = new LinkFieldData { Value = "2" };
            added.Fields["Memo"] = new TextFieldData { Value = "メモ2" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItemDetail", Id = tempId, Add = [added] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItemDetail", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItemDetail", Id = "1" }],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update"), ("Order", "1", "Update"), ("Order", "1", "Update") }));
            var last = Items(h[3].Snapshot);
            Assert.That(Rows(last.Single(e => EditHistorySnapshot.GetId(e) == "1"), "Details"), Is.Empty);
            Assert.That(Rows(last.Single(e => EditHistorySnapshot.GetId(e) == "2"), "Details").Select(e => Text(e, "Memo")), Is.EqualTo(new[] { "メモ2" }));
            Assert.That(_errors, Is.Empty);
        }

        //社員.部下 → 社員 (BossId = Id) の自己参照の従属。社員に履歴
        async Task AddEmployeeAsync(string rows)
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE employees (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, boss_id INTEGER)", new());
            await _db.ExecuteAsync(Ds, $"INSERT INTO employees (name, boss_id) VALUES {rows}", new());
            var employee = new ModuleDesign { Name = "Employee", DataSourceName = Ds, DbTable = "employees" };
            employee.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            employee.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            employee.Fields.Add(new IdFieldDesign { Name = "BossId", DbColumn = "boss_id", IsManualInput = true });
            employee.Fields.Add(new ListFieldDesign
            {
                Name = "Subordinates",
                CanUpdate = true,
                SearchCondition = new SearchCondition("Employee")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "BossId.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                },
            });
            employee.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            employee.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(employee);
        }

        static ModuleData Employee(string id, string name)
        {
            var data = new ModuleData { Name = "Employee" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            return data;
        }

        [Test]
        public async Task 自己参照の従属は子の保存で祖先すべてに版ができる()
        {
            //1 社長 ← 2 部長 ← 3 課長
            await AddEmployeeAsync("('社長', NULL), ('部長', 1), ('課長', 2)");
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "3", Update = [Employee("3", "係長")] }]));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EquivalentTo(new[] { ("Employee", "3", "Update"), ("Employee", "2", "Update"), ("Employee", "1", "Update") }),
                "自分の版 + 直接の上司 + その上司");
            var boss = h.Single(e => e.DataId == "1").Snapshot;
            var sub = Rows(boss, "Subordinates").Single();
            Assert.That(Text(Rows(sub, "Subordinates").Single(), "Name"), Is.EqualTo("係長"), "祖先のスナップショットにも新しい値");

            //同じ内容でもう一度保存しても、内容の変わらない祖先には版を作らない
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "3", Update = [Employee("3", "係長")] }]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(4), "3 自身の保存の版 (Update) だけ増える");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 自己参照のモジュール自身の画面からの保存でも祖先に版ができる()
        {
            //部長 (2) の画面で、自分と部下 (3) を保存する
            await AddEmployeeAsync("('社長', NULL), ('部長', 1), ('課長', 2)");
            var sub = Employee("3", "係長");
            sub.Fields["BossId"] = new IdFieldData { Value = "2" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "2", Update = [Employee("2", "次長"), sub] }]));

            var h = await HistoriesAsync();
            var boss = h.Single(e => e.DataId == "1").Snapshot;
            var s2 = Rows(boss, "Subordinates").Single();
            Assert.That((Text(s2, "Name"), Text(Rows(s2, "Subordinates").Single(), "Name")), Is.EqualTo(("次長", "係長")), "祖先 (1) の版");
            Assert.That(Text(h.Last(e => e.DataId == "2").Snapshot, "Name"), Is.EqualTo("次長"), "画面のレコード (2) の版");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 自己参照の画面から部下の行を一緒に保存しても_画面のレコードのIdと以前の版は変わらない()
        {
            await AddEmployeeAsync("('社長', NULL), ('部長', 1), ('課長', 2)");
            //部長 (2) の版を 1 つ作っておく
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "2", Update = [Employee("2", "部長改")] }]));
            Assert.That((await HistoriesAsync()).Where(e => e.DataId == "2").Select(e => Text(e.Snapshot, "Name")), Is.EqualTo(new[] { "部長改" }));

            //部長 (2) の画面で、自分と部下 (3) を保存する (部下の行も同じモジュール)
            var sub = Employee("3", "係長");
            sub.Fields["BossId"] = new IdFieldData { Value = "2" };
            var results = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "2", Update = [Employee("2", "次長"), sub] }]);
            AssertNoError(results);
            var h = await HistoriesAsync();
            Assert.Multiple(() =>
            {
                Assert.That(results[0].DestinationId, Is.EqualTo("2"), "保存後の遷移先は画面のレコード (Id は変えていない)");
                Assert.That(h.Where(e => e.DataId == "2").Select(e => Text(e.Snapshot, "Name")), Is.EqualTo(new[] { "部長改", "次長" }), "2 の版は 2 のまま");
                Assert.That(h.Where(e => e.DataId == "3").Select(e => Text(e.Snapshot, "Name")), Has.None.EqualTo("部長改"), "2 の版が 3 に付け替わらない");
            });
        }

        [Test]
        public async Task 自己参照の従属のデータが循環していても止まる()
        {
            //1 ← 2 ← 1 (データの誤りで循環)
            await AddEmployeeAsync("('A', 2), ('B', 1)");
            var submit = CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "1", Update = [Employee("1", "A改")] }]);
            Assert.That(await Task.WhenAny(submit, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(submit), "ループしない");
            AssertNoError(await submit);
            Assert.That((await HistoriesAsync()).Select(e => e.DataId), Is.EquivalentTo(new[] { "1", "2" }), "自分 + 上司 (2)。2 の上司は 1 (自分) で止まる");
        }

        // ===== 11: 履歴を持たない親 =====

        [Test]
        public async Task 履歴を持たない親の従属レコードの行の保存は何も起きない()
        {
            _design = EditHistoryTestDesigns.Create(withHistoryField: false);
            //インターセプタが動く (履歴モジュール = 契約フィールドはある) 状態で、親に EditHistoryField が無い
            await CreateOrderAsync();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            Assert.That(await HistoriesAsync(), Is.Empty);
            Assert.That(_errors, Is.Empty);
        }

        // ===== 12 〜 13: 持ち主が 2 つ =====

        [Test]
        public async Task 同じ行モジュールを2つの履歴つきの親が版に含めると設計チェックが両方に出て行の保存は失敗しロールバックする()
        {
            AddQuote(_design, withItems: true, withCustomer: false);
            var order = Check(_design, "Order").Where(e => e.Code == MultipleOwners).ToList();
            var quote = Check(_design, "Quote").Where(e => e.Code == MultipleOwners).ToList();
            Assert.That(order.Count, Is.EqualTo(1));
            Assert.That(quote.Count, Is.EqualTo(1));
            order[0].AssertFieldLocation("Order", "History", nameof(EditHistoryFieldDesign.RecordRowsSavedElsewhere));
            quote[0].AssertFieldLocation("Quote", "History", nameof(EditHistoryFieldDesign.RecordRowsSavedElsewhere));
            Assert.That(order[0].Message, Does.Contain("OrderItem").And.Contain("Quote"));
            Assert.That(quote[0].Message, Does.Contain("OrderItem").And.Contain("Order"));

            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '品X', 1)", new());

            //更新: 失敗し、行は変わらない
            var update = await UpdateItemAsync(ItemData("1", "1", "品X改", 9));
            AssertFailed(update);
            Assert.That(update[0].ExceptionMessage, Does.Contain("OrderItem"));
            Assert.That(await ScalarAsync("SELECT name FROM order_items WHERE id = 1"), Is.EqualTo("品X"), "ロールバック");

            //追加: 失敗し、行は増えない
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertFailed(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items"), Is.EqualTo(1), "ロールバック");

            //削除: 失敗し、行は残る
            AssertFailed(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "1" }],
            }]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items"), Is.EqualTo(1), "ロールバック");
            Assert.That(await HistoriesAsync(), Is.Empty);
        }

        [Test]
        public async Task 二つ目の親で除外か行ごとにすれば設計チェックは消え行を保存できる()
        {
            AddQuote(_design, withItems: true, withCustomer: false);
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '品X', 1)", new());

            //除外
            HistoryOf(_design, "Quote").ExcludedOwnedRecords.Add("Items");
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }), "残った持ち主 (Order) の版");

            //行ごと (行のモジュールに自分の履歴)
            HistoryOf(_design, "Quote").ExcludedOwnedRecords.Clear();
            HistoryOf(_design, "Quote").IndividuallyRecordedOwnedRecords.Add("Items");
            _design.Modules.Find("OrderItem")!.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            HistoryOf(_design, "Order").IndividuallyRecordedOwnedRecords.Add("Items");
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改2", 9)));
            Assert.That((await HistoriesAsync()).Skip(1).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("OrderItem", "1", "Update") }));

            //Quote だけ行ごと (Order は含める): 持ち主は Order 1 つ
            HistoryOf(_design, "Order").IndividuallyRecordedOwnedRecords.Clear();
            _design.Modules.Find("OrderItem")!.Fields.RemoveAll(e => e is EditHistoryFieldDesign);
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            _errors.Clear();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改3", 9)));
            var h = await HistoriesAsync();
            Assert.That(h.Skip(2).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }));
            Assert.That(Text(Items(h[2].Snapshot).Single(), "Name"), Is.EqualTo("品X改3"));
        }

        [Test]
        public async Task 埋め込みモジュールの子を2つの履歴つきの親が共有していても設計チェック12は出ない()
        {
            _design = EditHistoryTestDesigns.Create(withCustomer: true);
            AddQuote(_design, withItems: false, withCustomer: true);
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));

            //埋め込みの子の保存も失敗しない (親が子を指す = 行から親を引く対象ではない)
            await _db.ExecuteAsync(Ds, "INSERT INTO customers (name) VALUES ('顧客A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title, customer_id) VALUES ('受注A', 1)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO quotes (title, customer_id) VALUES ('見積A', 1)", new());
            var customer = new ModuleData { Name = "Customer" };
            customer.Fields["Id"] = new IdFieldData { Value = "1" };
            customer.Fields["Name"] = new TextFieldData { Value = "顧客A改" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Customer", Id = "1", Update = [customer] }]));
            Assert.That(await ScalarAsync("SELECT name FROM customers WHERE id = 1"), Is.EqualTo("顧客A改"));
        }

        // ===== 14 〜 16 =====

        [Test]
        public async Task 内容が変わらない保存は親の版にならない()
        {
            await CreateOrderAsync();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X", 1)));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1), "この保存で親の内容が変わらない (保存の前と後が同じ)");

            //変えたら版になり、同じ内容でもう一度保存しても増えない
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 1)));
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 1)));
            Assert.That((await HistoriesAsync()).Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));
        }

        [Test]
        public async Task 行の一括追加_一括INSERTに乗る件数_でも親の版ができる()
        {
            await CreateOrderAsync();
            var threshold = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 1;
            try
            {
                //取込と同じ入口 (Id 空の ModuleData → NoTemporaryIdResolution = true の投げ切りの Add)
                var rows = Enumerable.Range(1, 3).Select(i =>
                {
                    var d = new ModuleData { Name = "OrderItem" };
                    d.Fields["Order"] = new LinkFieldData { Value = "1" };
                    d.Fields["Name"] = new TextFieldData { Value = $"取込{i}" };
                    d.Fields["Qty"] = new NumberFieldData { Value = i };
                    return d;
                }).ToList();
                AssertNoError(await CreateIO().SubmitWithTransactionByModuleDataAsync("OrderItem", rows));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = threshold;
            }

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }), "取込 1 回で親の版 1 つ");
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X", "品Y", "取込1", "取込2", "取込3" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 親が論理削除済みのときの行の保存は版にならず保存は成功する()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "UPDATE orders SET is_deleted = 1 WHERE id = 1", new());

            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            Assert.That(await ScalarAsync("SELECT name FROM order_items WHERE id = 1"), Is.EqualTo("品X改"));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            Assert.That((await HistoriesAsync()).Select(e => e.ChangeType), Is.EqualTo(new[] { "Add" }));
            Assert.That(_errors, Is.Empty);
        }

        // ===== 同じ親の複数の一覧・埋め込みの中の一覧 =====

        //受注に「明細」(数量 1 以上) と「付属品」(数量 0) の 2 つの一覧。どちらも OrderItem で Order を指す
        void AddAccessories()
        {
            var order = _design.Modules.Find("Order")!;
            order.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items").SearchCondition.Condition = MultiMatchCondition.And(
                new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                new FieldValueMatchCondition { SearchTargetVariable = "Qty.Value", Comparison = MatchComparison.GreaterThanOrEqual, Value = MultiTypeValue.Create(1m) });
            order.Fields.Add(new ListFieldDesign
            {
                Name = "Accessories",
                DeleteTogether = true,
                SearchCondition = new SearchCondition("OrderItem")
                {
                    Condition = MultiMatchCondition.And(
                        new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                        new FieldValueMatchCondition { SearchTargetVariable = "Qty.Value", Comparison = MatchComparison.LessThan, Value = MultiTypeValue.Create(1m) }),
                },
            });
        }

        [Test]
        public async Task 同じ親の2つの一覧が同じ行モジュールを含むとき設計チェック12は出ず_どちらの一覧の行の保存も親の1版になる()
        {
            AddAccessories();
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners), "持ち主の親は Order 1 つ (宣言が 2 つあるだけ)");

            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '付属品', 0)", new());

            //付属品の一覧の行
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "付属品改", 0)));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(Rows(h[1].Snapshot, "Accessories").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "付属品改" }));

            //明細の一覧の行
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 1)));
            h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update", "Update" }));
            Assert.That(Items(h[2].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改", "品Y" }));

            //付属品 → 明細 へ移る更新 (数量 0 → 5) も 1 版
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "付属品改", 5)));
            h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update", "Update", "Update" }));
            Assert.That(Items(h[3].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改", "品Y", "付属品改" }));
            Assert.That(Rows(h[3].Snapshot, "Accessories"), Is.Empty);

            //明細 → 付属品 へ戻す更新 (数量 5 → 0) も 1 版
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "付属品改", 0)));
            h = await HistoriesAsync();
            Assert.That(h.Count, Is.EqualTo(5));
            Assert.That(Rows(h[4].Snapshot, "Accessories").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "付属品改" }));
            Assert.That(_errors, Is.Empty);
        }

        //受注.顧客 (埋め込み) → 顧客.担当者 (一覧・担当者が顧客を指す)
        void AddCustomerContacts()
        {
            _design.Modules.Find("Customer")!.Fields.Add(new ListFieldDesign
            {
                Name = "Contacts",
                DeleteTogether = true,
                SearchCondition = new SearchCondition("CustomerContact")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Customer.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                },
            });
            var contact = new ModuleDesign { Name = "CustomerContact", DataSourceName = Ds, DbTable = "customer_contacts" };
            contact.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            contact.Fields.Add(new LinkFieldDesign { Name = "Customer", SearchCondition = new SearchCondition("Customer"), DbColumn = "customer_id" });
            contact.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            contact.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(contact);
        }

        static ModuleData ContactData(string id, string name)
        {
            var row = new ModuleData { Name = "CustomerContact" };
            row.Fields["Id"] = new IdFieldData { Value = id };
            row.Fields["Customer"] = new LinkFieldData { Value = "1" };
            row.Fields["Name"] = new TextFieldData { Value = name };
            return row;
        }

        //担当者を単独の送信で 更新・追加・削除 する (どれも保存は成功)
        async Task SaveContactsAsync()
        {
            await _db.ExecuteAsync(Ds, "INSERT INTO customers (name) VALUES ('顧客A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO customer_contacts (customer_id, name) VALUES (1, '担当A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title, customer_id) VALUES ('受注A', 1)", new());

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "CustomerContact", Id = "1", Update = [ContactData("1", "担当A改")] }]));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "CustomerContact", Id = tempId, Add = [ContactData(tempId, "担当B")] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "CustomerContact", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "CustomerContact", Id = "1" }],
            }]));
            Assert.That(await ScalarAsync("SELECT GROUP_CONCAT(name) FROM customer_contacts"), Is.EqualTo("担当B"), "保存はされている");
        }

        [Test]
        public async Task 埋め込みモジュールの中の一覧の行の保存は版にならず保存は成功する()
        {
            _design = EditHistoryTestDesigns.Create(withCustomer: true);
            AddCustomerContacts();
            await SaveContactsAsync();
            Assert.That(await HistoriesAsync(), Is.Empty, "親までたどれない宣言 = 行の保存では版を作らない");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 埋め込みの子を別の履歴つきの親も埋め込んでいても_中の一覧の行で設計チェック12は出ず保存も成功する()
        {
            _design = EditHistoryTestDesigns.Create(withCustomer: true);
            AddCustomerContacts();
            AddQuote(_design, withItems: false, withCustomer: true);
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            await _db.ExecuteAsync(Ds, "INSERT INTO quotes (title, customer_id) VALUES ('見積A', 1)", new());
            await SaveContactsAsync();
            Assert.That(await HistoriesAsync(), Is.Empty);
            Assert.That(_errors, Is.Empty);
        }

        // ===== 一括 INSERT の申告 =====

        //レコーダーの内側に置いて、本体に渡る送信の申告 (NoTemporaryIdResolution) を見る
        sealed class SubmitSpy : IModuleDataIOInterceptor
        {
            public List<(string Module, bool NoTemporaryIdResolution)> Seen { get; } = new();
            public Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
            {
                Seen.AddRange(transactionData.Select(e => (e.ModuleName, e.NoTemporaryIdResolution)));
                return next();
            }
            public Task GetListAsync(ModuleDataIO io, SearchCondition condition, Paging<ModuleData> result) => Task.CompletedTask;
        }

        [Test]
        public async Task 履歴つきの親の版に含まれる行モジュールの追加は一括INSERTの申告を外し_含まれないモジュールは外さない()
        {
            _design = EditHistoryTestDesigns.Create(withCustomer: true);
            await CreateOrderAsync();
            var spy = new SubmitSpy();
            ModuleDataIO IO()
            {
                var io = CreateIO();
                io.AddInterceptor(spy);
                return io;
            }
            static List<ModuleData> Rows(string module, Action<ModuleData> fill) => Enumerable.Range(1, 3).Select(i =>
            {
                var d = new ModuleData { Name = module };
                fill(d);
                return d;
            }).ToList();

            var threshold = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 1;
            try
            {
                AssertNoError(await IO().SubmitWithTransactionByModuleDataAsync("OrderItem", Rows("OrderItem", d =>
                {
                    d.Fields["Order"] = new LinkFieldData { Value = "1" };
                    d.Fields["Name"] = new TextFieldData { Value = "取込" };
                })));
                AssertNoError(await IO().SubmitWithTransactionByModuleDataAsync("Customer", Rows("Customer", d => d.Fields["Name"] = new TextFieldData { Value = "顧客" })));
                AssertNoError(await IO().SubmitWithTransactionByModuleDataAsync("AppUser", Rows("AppUser", d => d.Fields["Name"] = new TextFieldData { Value = "利用者" })));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = threshold;
            }

            Assert.That(spy.Seen.Where(e => e.Module == "OrderItem").Select(e => e.NoTemporaryIdResolution), Is.EqualTo(new[] { false, false, false }),
                "Order の版に含まれる OrderItem の追加は 1 行ずつの経路 (採番 Id から親を引く)");
            Assert.That(spy.Seen.Where(e => e.Module == "Customer").Select(e => e.NoTemporaryIdResolution), Is.EqualTo(new[] { true, true, true }),
                "埋め込み (親が子を指す) の子モジュールは対象外 = 一括 INSERT のまま");
            Assert.That(spy.Seen.Where(e => e.Module == "AppUser").Select(e => e.NoTemporaryIdResolution), Is.EqualTo(new[] { true, true, true }),
                "履歴にも親の版にも関係しないモジュールは一括 INSERT のまま");
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.ChangeType)), Is.EqualTo(new[] { ("Order", "Add"), ("Order", "Update") }));
        }

        // ===== Id の型 =====

        [Test]
        public async Task 整数の自動採番のIdの列でも行から親を引ける()
        {
            //このテストの orders / order_items の id は INTEGER PRIMARY KEY AUTOINCREMENT。行から親を引く検索の値は文字列の Id で渡る
            var types = (await _db.QueryAsync(Ds, "SELECT name, type FROM pragma_table_info('order_items') WHERE name IN ('id', 'order_id') ORDER BY name", new()))
                .Select(e => (e["name"]?.ToString(), e["type"]?.ToString())).ToList();
            Assert.That(types, Is.EqualTo(new[] { ((string?)"id", (string?)"INTEGER"), ("order_id", "INTEGER") }));
            await CreateOrderAsync();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update", "Update", "Update" }));
            Assert.That(Items(h[3].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改", "品Z" }));
        }

        // ===== レビュー指摘の修正分 =====

        //受注が 明細 OrderItem を 埋め込み (MainItem) と 一覧 (Items) の両方で持ち、明細が 内訳 (Details) を持つ。embedFirst = 埋め込みのフィールドを一覧より前に定義する
        void EmbedAndListDesign(bool embedFirst)
        {
            _design = EditHistoryTestDesigns.Create(withDetails: true);
            var order = _design.Modules.Find("Order")!;
            var index = order.Fields.FindIndex(e => e.Name == "Items");
            order.Fields.Insert(embedFirst ? index : index + 1, new ModuleFieldDesign { Name = "MainItem", DbColumn = "item_id", ModuleName = "OrderItem", LayoutName = "" });
            var grid = new GridLayoutDesign();
            var row = new GridRow();
            row.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign("Name") });
            grid.Rows.Add(row);
            _design.Modules.Find("OrderItem")!.DetailLayouts[""] = new DetailLayoutDesign { Layout = grid };
            var names = order.Fields.Select(e => e.Name).ToList();
            Assert.That(names.IndexOf("MainItem") < names.IndexOf("Items"), Is.EqualTo(embedFirst), "フィールドの定義順");
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task 埋め込みと一覧の両方で辿る明細の内訳を単独で保存すると_定義順に関係なく受注の版になる(bool embedFirst)
        {
            EmbedAndListDesign(embedFirst);
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO order_item_details (item_id, memo) VALUES (1, 'メモ1')", new());

            var detail = new ModuleData { Name = "OrderItemDetail" };
            detail.Fields["Id"] = new IdFieldData { Value = "1" };
            detail.Fields["Item"] = new LinkFieldData { Value = "1" };
            detail.Fields["Memo"] = new TextFieldData { Value = "メモ改" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItemDetail", Id = "1", Update = [detail] }]));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            var itemX = Items(h[1].Snapshot).Single(e => EditHistorySnapshot.GetId(e) == "1");
            Assert.That(Rows(itemX, "Details").Select(e => Text(e, "Memo")), Is.EqualTo(new[] { "メモ改" }), "一覧 Items の行の下の内訳");
            Assert.That(_errors, Is.Empty);
        }

        //標準の追加の代わりに SQL で受注を作る (NewId 無し = 本体は作ったレコードの Id を返せない)
        void CreateOrderBySql()
        {
            var sql = new ExecuteSqlFieldDesign { Name = "Create", Timing = ExecuteSqlTiming.Create, WithStandardIO = ExecuteSqlWithStandardIO.None };
#pragma warning disable CS0612 // テストはデザインをコードで組むので SQL は SqlText で入れる (SetSqlText は本体の internal)
            sql.ExecuteSqlSetting.SqlText = "INSERT INTO orders (title) VALUES ('SQLで作成')";
#pragma warning restore CS0612
            _design.Modules.Find("Order")!.Fields.Add(sql);
        }

        static ModuleSubmitData OrderAddBySql()
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            var editing = OrderData(tempId, "SQLで作成");
            editing.Fields["Items"] = new ListFieldData();
            editing.Fields["Related"] = new ListFieldData();
            return new ModuleSubmitData { ModuleName = "Order", Id = tempId, CurrentEditingData = editing, Add = [OrderData(tempId, "SQLで作成")] };
        }

        [Test]
        public async Task 保存後のIdが分からない新規作成_ExecuteSqlのCreateでNewId無し_は版を作らずログに残す()
        {
            CreateOrderBySql();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([OrderAddBySql()]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM orders"), Is.EqualTo(1), "SQL で作られている");
            Assert.That(await HistoriesAsync(), Is.Empty, "Update を最初の版にしない");
            Assert.That(_errors.Count, Is.EqualTo(1), "記録できなかったことはログに残る");
            Assert.That(_errors[0], Does.Contain("Order"));
        }

        [Test]
        public async Task 保存後のIdが分からない新規作成と同じ保存で_既存の受注の明細を変えた分は既存の受注の版になる()
        {
            CreateOrderBySql();
            //既存の受注 1 (明細つき・版 1 つ) は画面からの保存と同じ形で作る (SQL の Create を使わない = 先に作ってから SQL の設定を足す)
            _design.Modules.Find("Order")!.Fields.RemoveAll(e => e is ExecuteSqlFieldDesign);
            await CreateOrderAsync();
            CreateOrderBySql();

            //1 つのトランザクションに: SQL で受注を新規作成 + 既存の受注 1 の明細の更新 (行のモジュールの送信)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([
                OrderAddBySql(),
                new ModuleSubmitData { ModuleName = "OrderItem", Id = "1", Update = [ItemData("1", "1", "品X改", 9)] },
            ]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }),
                "Id が分からないのは新規作成の受注だけ。既存の受注 1 の明細の変更は版になる");
        }

        [Test]
        public async Task 手入力Idの行で行のモジュールの送信がIdと親へのリンクを同時に変えると元の親と新しい親の両方の版になる()
        {
            ((IdFieldDesign)_design.Modules.Find("OrderItem")!.Fields.First(e => e.Name == "Id")).IsManualInput = true;
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (id, title) VALUES (1, '受注A'), (2, '受注B')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (id, order_id, name, qty) VALUES (1, 1, '品X', 1), (2, 1, '品Y', 2), (3, 2, '品Z', 3)", new());

            //明細 1 を Id 100 にし、受注B へ付け替える
            var results = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "1", Update = [ItemData("@replace:1->100", "2", "品X", 1)],
            }]);
            AssertNoError(results);
            Assert.That((await _db.QueryAsync(Ds, "SELECT order_id FROM order_items WHERE id = 100", new())).Single()["order_id"]!.ToString(), Is.EqualTo("2"));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EquivalentTo(new[] { ("Order", "1", "Update"), ("Order", "2", "Update") }));
            Assert.That(Items(h.Single(e => e.DataId == "1").Snapshot).Select(e => EditHistorySnapshot.GetId(e)), Is.EqualTo(new[] { "2" }), "元の親から消える");
            Assert.That(Items(h.Single(e => e.DataId == "2").Snapshot).Select(e => EditHistorySnapshot.GetId(e)), Is.EquivalentTo(new[] { "3", "100" }), "新しい親に新しい Id で入る");
            Assert.That(_errors, Is.Empty);
        }

        // ===== 検索条件での削除・一緒に消える行 =====

        [Test]
        public async Task 検索条件での削除で消えた行の親それぞれに版ができる()
        {
            await CreateOrderAsync("受注A");
            await CreateOrderAsync("受注B");
            //品Y は受注A (2) と受注B (4) の両方にある
            static ModuleSubmitData DeleteByName(string name) => new()
            {
                ModuleName = "OrderItem",
                SearchDelete = [new SearchCondition("OrderItem")
                {
                    Condition = new FieldValueMatchCondition { SearchTargetVariable = "Name.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(name) },
                }],
            };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([DeleteByName("品Y")]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE name = '品Y'"), Is.EqualTo(0));
            var h = await HistoriesAsync();
            Assert.That(h.Skip(2).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EquivalentTo(new[] { ("Order", "1", "Update"), ("Order", "2", "Update") }));
            Assert.That(h.Skip(2).All(e => Items(e.Snapshot).Select(x => Text(x, "Name")).SequenceEqual(new[] { "品X" })), Is.True, "消えた行はスナップショットに無い");

            //条件に合う行が無い削除は版にならない
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([DeleteByName("無い品")]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(4));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 履歴を持たないレコードの削除で一緒に消えた行は_その行を含む履歴つきの親の版になる()
        {
            //見積 Q (履歴なし) の明細一覧も OrderItem (quote_id)。Q を消すと Q の明細は一緒に消える
            AddQuote(_design, withItems: true, withCustomer: false, withHistory: false);
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners), "履歴の無い親は数えない");
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO quotes (title) VALUES ('見積Q')", new());
            await _db.ExecuteAsync(Ds, "UPDATE order_items SET quote_id = 1 WHERE id = 2", new());

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Quote", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Quote", Id = "1" }],
            }]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items"), Is.EqualTo(1), "見積の明細 (2) が一緒に消えた");
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EqualTo(new[] { "品X" }));
            Assert.That(_errors, Is.Empty);
        }

        // ===== Id 以外・複数の項目での結び付き =====

        //部門 (Category: 履歴あり) が 所属 (Member) を Code (と Branch) で持つ
        async Task CategoryDesignAsync(bool twoKeys)
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE categories (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT, branch TEXT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE members (id INTEGER PRIMARY KEY AUTOINCREMENT, parent_code TEXT, parent_branch TEXT, name TEXT)", new());
            var category = new ModuleDesign { Name = "Category", DataSourceName = Ds, DbTable = "categories" };
            category.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            category.Fields.Add(new TextFieldDesign { Name = "Code", DbColumn = "code" });
            category.Fields.Add(new TextFieldDesign { Name = "Branch", DbColumn = "branch" });
            category.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            MatchConditionBase condition = new FieldVariableMatchCondition { SearchTargetVariable = "ParentCode.Value", Comparison = MatchComparison.Equal, Variable = "Code.Value" };
            if (twoKeys)
                condition = MultiMatchCondition.And(condition,
                    new FieldVariableMatchCondition { SearchTargetVariable = "ParentBranch.Value", Comparison = MatchComparison.Equal, Variable = "Branch.Value" });
            category.Fields.Add(new ListFieldDesign { Name = "Members", DeleteTogether = true, SearchCondition = new SearchCondition("Member") { Condition = condition } });
            category.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            category.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(category);

            var member = new ModuleDesign { Name = "Member", DataSourceName = Ds, DbTable = "members" };
            member.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            member.Fields.Add(new TextFieldDesign { Name = "ParentCode", DbColumn = "parent_code" });
            member.Fields.Add(new TextFieldDesign { Name = "ParentBranch", DbColumn = "parent_branch" });
            member.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            member.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(member);

            //(1) C01/東京 (2) C01/大阪 (3) C02/東京
            await _db.ExecuteAsync(Ds, "INSERT INTO categories (code, branch, name) VALUES ('C01', '東京', '営業東京'), ('C01', '大阪', '営業大阪'), ('C02', '東京', '開発')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO members (parent_code, parent_branch, name) VALUES ('C01', '大阪', '山田'), ('C02', '東京', '佐藤')", new());
        }

        static ModuleData MemberData(string id, string code, string branch, string name)
        {
            var data = new ModuleData { Name = "Member" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["ParentCode"] = new TextFieldData { Value = code };
            data.Fields["ParentBranch"] = new TextFieldData { Value = branch };
            data.Fields["Name"] = new TextFieldData { Value = name };
            return data;
        }

        [Test]
        public async Task 親のId以外の項目で結び付く宣言でも行から親を引ける()
        {
            await CategoryDesignAsync(twoKeys: false);
            //C02 の 佐藤 (2) を更新 → C02 の部門 (3) の版
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Member", Id = "2", Update = [MemberData("2", "C02", "東京", "佐藤改")] }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Category", "3", "Update") }));
            Assert.That(Rows(h[0].Snapshot, "Members").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "佐藤改" }));

            //C02 → C01 へ付け替え: 元の 3 と、Code が C01 の部門 (1, 2) に版 (1 項目の結び付きなので C01 の部門は両方とも親)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Member", Id = "2", Update = [MemberData("2", "C01", "東京", "佐藤改")] }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(1).Select(e => (e.Module, e.DataId)), Is.EquivalentTo(new[] { ("Category", "1"), ("Category", "2"), ("Category", "3") }));

            //1 回の送信で複数行: 山田 (C01) の更新 + 佐藤 (C01 → C02) の付け替え → C01 の部門 (1, 2) と C02 の部門 (3) がそれぞれ 1 版
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Member", Id = "1", Update = [MemberData("1", "C01", "大阪", "山田改"), MemberData("2", "C02", "東京", "佐藤改2")],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(4).Select(e => (e.Module, e.DataId)), Is.EquivalentTo(new[] { ("Category", "1"), ("Category", "2"), ("Category", "3") }));
            Assert.That(Rows(h.Skip(4).Single(e => e.DataId == "1").Snapshot, "Members").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "山田改" }));
            Assert.That(Rows(h.Skip(4).Single(e => e.DataId == "2").Snapshot, "Members").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "山田改" }));
            Assert.That(Rows(h.Skip(4).Single(e => e.DataId == "3").Snapshot, "Members").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "佐藤改2" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 二つの項目で結び付く宣言でも行から親を引ける()
        {
            await CategoryDesignAsync(twoKeys: true);
            //C01/大阪 の 山田 (1) を更新 → 部門 2 だけ (C01/東京 の 1 は親ではない)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Member", Id = "1", Update = [MemberData("1", "C01", "大阪", "山田改")] }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Category", "2", "Update") }));
            Assert.That(Rows(h[0].Snapshot, "Members").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "山田改" }));

            //大阪 → 東京 へ付け替え: 元の 2 と 新しい 1
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Member", Id = "1", Update = [MemberData("1", "C01", "東京", "山田改")] }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(1).Select(e => (e.Module, e.DataId)), Is.EquivalentTo(new[] { ("Category", "1"), ("Category", "2") }));
            Assert.That(Rows(h.Skip(1).Single(e => e.DataId == "1").Snapshot, "Members").Select(e => Text(e, "Name")), Is.EqualTo(new[] { "山田改" }));
            Assert.That(_errors, Is.Empty);
        }

        // ===== 2 回目のレビュー指摘の修正分 =====

        //受注の明細の一覧に「担当が現在のユーザー」の条件を足す (OrderItem.Assignee → AppUser)
        async Task AddAssigneeAsync()
        {
            await _db.ExecuteAsync(Ds, "ALTER TABLE order_items ADD COLUMN assignee_id INTEGER", new());
            _design.Modules.Find("OrderItem")!.Fields.Add(new LinkFieldDesign { Name = "Assignee", SearchCondition = new SearchCondition("AppUser"), DbColumn = "assignee_id" });
            _design.Modules.Find("Order")!.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items").SearchCondition.Condition = MultiMatchCondition.And(
                new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                new FieldVariableMatchCondition { SearchTargetVariable = "Assignee.Value", Comparison = MatchComparison.Equal, Variable = "CurrentUser.Id.Value" });
        }

        [Test]
        public async Task 一覧の条件に現在のユーザーの条件があっても行の保存は親の版になり例外にならない()
        {
            await AddAssigneeAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            //品X・品Y は現在のユーザー (7) の担当、品Z は他の人 (8) の担当
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty, assignee_id) VALUES (1, '品X', 1, 7), (1, '品Y', 2, 7), (1, '品Z', 3, 8)", new());

            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }));
            Assert.That(Items(h[0].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改", "品Y" }));

            //一覧に入らない (他の人の担当の) 行の更新は版にならない
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "品Z改", 3)));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1));

            //現在のユーザーの担当の行の追加は版になる
            var tempId = "@temporary:" + Guid.NewGuid();
            var added = ItemData(tempId, "1", "品W", 4);
            added.Fields["Assignee"] = new LinkFieldData { Value = "7" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [added] }]));
            h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Update", "Update" }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改", "品Y", "品W" }));
            Assert.That(_errors, Is.Empty);
        }

        //結び付きとして読めない条件 (Or の中・否定の中の変数条件、等しい以外の比較) を持つ明細の一覧
        static MatchConditionBase UnreadableBinding(string kind)
        {
            var order = new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" };
            var name = new FieldVariableMatchCondition { SearchTargetVariable = "Name.Value", Comparison = MatchComparison.Equal, Variable = "Title.Value" };
            return kind switch
            {
                "Or" => MultiMatchCondition.Or(order, name),
                "Not" => MultiMatchCondition.And(order, new MultiMatchCondition { IsNot = true, Children = [name] }),
                "比較" => MultiMatchCondition.And(order,
                    new FieldVariableMatchCondition { SearchTargetVariable = "Qty.Value", Comparison = MatchComparison.GreaterThanOrEqual, Variable = "Amount.Value" }),
                _ => throw new ArgumentException(kind),
            };
        }

        [TestCase("Or")]
        [TestCase("Not")]
        [TestCase("比較")]
        public async Task 結び付きとして読めない条件の一覧は行から親を引かず_保存は成功し版にならず設計チェック12にも数えない(string kind)
        {
            _design.Modules.Find("Order")!.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items").SearchCondition.Condition = UnreadableBinding(kind);
            await CreateOrderAsync();
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            Assert.That(await ScalarAsync("SELECT GROUP_CONCAT(name) FROM order_items ORDER BY id"), Is.EqualTo("品X改,品Z"), "保存はされている");
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.ChangeType)), Is.EqualTo(new[] { ("Order", "Add") }), "親の画面からの作成の版だけ");
            Assert.That(_errors, Is.Empty);

            //同じ行モジュールを含む履歴つきの親 (見積) を足しても、受注の宣言は数えないので設計チェック 12 は出ず、行の保存は見積の版になる
            AddQuote(_design, withItems: true, withCustomer: false);
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            await _db.ExecuteAsync(Ds, "INSERT INTO quotes (title) VALUES ('見積Q')", new());
            await _db.ExecuteAsync(Ds, "UPDATE order_items SET quote_id = 1 WHERE id = 1", new());
            var item = ItemData("1", "1", "品X改2", 9);
            item.Fields["Quote"] = new LinkFieldData { Value = "1" };
            AssertNoError(await UpdateItemAsync(item));
            var h = await HistoriesAsync();
            Assert.That(h.Skip(1).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Quote", "1", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EqualTo(new[] { "品X改2" }));
            Assert.That(_errors, Is.Empty);
        }

        // ----- 版が 1 つも無い親 (履歴フィールドを後から足した等) -----

        [Test]
        public async Task 版の無い親で一覧の条件に合わない行の更新は版にならない()
        {
            _design.Modules.Find("Order")!.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items").SearchCondition.Condition = MultiMatchCondition.And(
                new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                new FieldValueMatchCondition { SearchTargetVariable = "Qty.Value", Comparison = MatchComparison.GreaterThanOrEqual, Value = MultiTypeValue.Create(1m) });
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '品X', 1), (1, '対象外', 0)", new());

            //条件に合わない行 (数量 0) の更新
            AssertNoError(await UpdateItemAsync(ItemData("2", "1", "対象外改", 0)));
            Assert.That(await ScalarAsync("SELECT name FROM order_items WHERE id = 2"), Is.EqualTo("対象外改"));
            Assert.That(await HistoriesAsync(), Is.Empty, "保存の前に引いた親は今の内容と比べる (一覧に入らない行は親の内容を変えない)");

            //条件に合わない行の追加 (保存の後にだけ引ける親)
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "対象外2", 0)] }]));
            Assert.That(await HistoriesAsync(), Is.Empty, "書いた行が親の内容に入っていない");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 版の無い親で一覧に入る行を更新追加削除するとそれぞれ版になる()
        {
            //受注 1・2・3 (どれも版が無い) に明細を 2 行ずつ
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A'), ('受注B'), ('受注C')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, 'A1', 1), (1, 'A2', 2), (2, 'B1', 1), (2, 'B2', 2), (3, 'C1', 1), (3, 'C2', 2)", new());

            //更新 (受注 1)
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "A1改", 1)));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }));
            Assert.That(Items(h[0].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "A1改", "A2" }));

            //追加 (受注 2)
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "2", "B3", 3)] }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(1).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "2", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "B1", "B2", "B3" }));

            //削除 (受注 3)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "6", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "6" }],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(2).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "3", "Update") }));
            Assert.That(Items(h[2].Snapshot).Select(e => Text(e, "Name")), Is.EqualTo(new[] { "C1" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 履歴を持たないレコードの削除で_一緒に消えない一覧の行を拾っても版の無い親は版にならない()
        {
            //見積 Q (履歴なし) の明細一覧は 一緒に消えない (DeleteTogether なし) が、更新できる = 従属レコードの宣言
            AddQuote(_design, withItems: true, withCustomer: false, withHistory: false);
            var quoteItems = _design.Modules.Find("Quote")!.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items");
            quoteItems.DeleteTogether = false;
            quoteItems.CanUpdate = true;
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO quotes (title) VALUES ('見積Q')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, quote_id, name, qty) VALUES (1, 1, '品X', 1), (1, NULL, '品Y', 2)", new());

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Quote", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Quote", Id = "1" }],
            }]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM quotes"), Is.EqualTo(0));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items"), Is.EqualTo(2), "一緒に消えない一覧の行は残る");
            Assert.That(await HistoriesAsync(), Is.Empty, "受注の内容は変わっていない");
            Assert.That(_errors, Is.Empty);
        }

        // ----- 検索条件での削除の権限 -----

        static ModuleSubmitData DeleteItemsByName(string name) => new()
        {
            ModuleName = "OrderItem",
            SearchDelete = [new SearchCondition("OrderItem")
            {
                Condition = new FieldValueMatchCondition { SearchTargetVariable = "Name.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(name) },
            }],
        };

        [TestCase("削除不可")]
        [TestCase("閲覧不可")]
        public async Task 削除か閲覧の権限の無い利用者の検索条件での削除は今までどおり拒否され版にならない(string kind)
        {
            await CreateOrderAsync("受注A");
            await CreateOrderAsync("受注B");
            var item = _design.Modules.Find("OrderItem")!;
            if (kind == "削除不可") item.CanDelete = false;
            else item.UserReadCondition = new ModuleMatchCondition
            {
                ModuleName = "AppUser",
                Condition = new FieldValueMatchCondition { SearchTargetVariable = "Id.Value", Comparison = MatchComparison.NotEqual, Value = MultiTypeValue.Create("7") },
            };

            //履歴のインターセプタを付けない本体だけの結果 (今までの結果の形)
            var plain = await new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>())).SubmitWithTransactionAsync([DeleteItemsByName("品Y")]);
            AssertFailed(plain);

            var results = await CreateIO().SubmitWithTransactionAsync([DeleteItemsByName("品Y")]);
            AssertFailed(results);
            Assert.That(results.Count, Is.EqualTo(plain.Count));
            Assert.That(results.Select(e => e.ExceptionMessage), Is.EqualTo(plain.Select(e => e.ExceptionMessage)), "拒否の結果は本体だけのときと同じ");
            Assert.That(results.Select(e => e.Exception?.GetType()), Is.EqualTo(plain.Select(e => e.Exception?.GetType())));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE name = '品Y'"), Is.EqualTo(2), "消えていない");
            Assert.That((await HistoriesAsync()).Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Add" }), "版は増えない");
        }

        // ----- 一括 INSERT の申告: 親と結び付く値の無い行だけなら外さない -----

        [Test]
        public async Task 親と結び付く値の空の行だけの一括追加は一括INSERTの申告を外さず_値のある行があれば外す()
        {
            await CreateOrderAsync();
            var spy = new SubmitSpy();
            ModuleDataIO IO()
            {
                var io = CreateIO();
                io.AddInterceptor(spy);
                return io;
            }
            static ModuleData Row(string? orderId, string name)
            {
                var d = new ModuleData { Name = "OrderItem" };
                if (orderId != null) d.Fields["Order"] = new LinkFieldData { Value = orderId == "" ? null : orderId };
                d.Fields["Name"] = new TextFieldData { Value = name };
                return d;
            }

            var threshold = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 1;
            try
            {
                //全部の行で親へのリンクが空 (項目なし・値が null)
                AssertNoError(await IO().SubmitWithTransactionByModuleDataAsync("OrderItem", [Row(null, "空1"), Row("", "空2"), Row(null, "空3")]));
                Assert.That(spy.Seen.Select(e => e.NoTemporaryIdResolution), Is.EqualTo(new[] { true, true, true }), "どの親の版にもならない = 一括 INSERT のまま");
                Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE name LIKE '空%'"), Is.EqualTo(3));
                Assert.That((await HistoriesAsync()).Select(e => e.ChangeType), Is.EqualTo(new[] { "Add" }));

                //値のある行が混じる
                spy.Seen.Clear();
                AssertNoError(await IO().SubmitWithTransactionByModuleDataAsync("OrderItem", [Row(null, "混1"), Row("1", "混2"), Row(null, "混3")]));
                Assert.That(spy.Seen.Select(e => e.NoTemporaryIdResolution), Is.EqualTo(new[] { true, false, true }), "値のある行の申告を外す (= 1 行ずつの経路になる)");
                Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE name LIKE '混%'"), Is.EqualTo(3));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = threshold;
            }
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => e.ChangeType), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X", "品Y", "混2" }));
            Assert.That(_errors, Is.Empty);
        }

        // ===== 最終レビュー指摘の修正分 =====

        //受注が 明細行 (OrderLine: 複合 Id = 受注 + 行番号) を一覧 Lines で持つ。親への結び付きは Order
        async Task AddOrderLinesAsync()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE order_lines (order_id INTEGER NOT NULL, line_no INTEGER NOT NULL, name TEXT, PRIMARY KEY (order_id, line_no))", new());
            var line = new ModuleDesign { Name = "OrderLine", DataSourceName = Ds, DbTable = "order_lines" };
            line.Fields.Add(new IdFieldDesign { Name = "Id", CompositeIdVariables = { "Order.Value", "LineNo.Value" }, CompositeIdSeparator = "-" });
            line.Fields.Add(new LinkFieldDesign { Name = "Order", SearchCondition = new SearchCondition("Order"), DbColumn = "order_id" });
            line.Fields.Add(new NumberFieldDesign { Name = "LineNo", DbColumn = "line_no" });
            line.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            line.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(line);
            _design.Modules.Find("Order")!.Fields.Add(new ListFieldDesign
            {
                Name = "Lines",
                DeleteTogether = true,
                CanCreate = true, CanUpdate = true, CanDelete = true,
                SearchCondition = new SearchCondition("OrderLine")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                },
            });
        }

        static ModuleData LineData(string? id, string orderId, int lineNo, string name)
        {
            var data = new ModuleData { Name = "OrderLine" };
            if (id != null) data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Order"] = new LinkFieldData { Value = orderId };
            data.Fields["LineNo"] = new NumberFieldData { Value = lineNo };
            data.Fields["Name"] = new TextFieldData { Value = name };
            return data;
        }

        static string[] LineNames(ModuleData snapshot) => Rows(snapshot, "Lines").Select(e => Text(e, "Name")!).ToArray();

        [Test]
        public async Task 複合Idの行モジュールの送信で行を更新追加削除すると親の版になる()
        {
            await AddOrderLinesAsync();
            await CreateOrderAsync("受注A");
            await CreateOrderAsync("受注B");
            await _db.ExecuteAsync(Ds, "INSERT INTO order_lines (order_id, line_no, name) VALUES (1, 1, 'L1'), (1, 2, 'L2'), (2, 1, 'M1')", new());

            //更新
            var results = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderLine", Id = "1-1", Update = [LineData("1-1", "1", 1, "L1改")] }]);
            AssertNoError(results);
            Assert.That(await ScalarAsync("SELECT name FROM order_lines WHERE order_id = 1 AND line_no = 1"), Is.EqualTo("L1改"));
            var h = await HistoriesAsync();
            Assert.That(h.Skip(2).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }));
            Assert.That(LineNames(h[2].Snapshot), Is.EquivalentTo(new[] { "L1改", "L2" }));

            //追加 (受注B へ)
            var tempId = IdFieldData.NewId().Value!;
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderLine", Id = tempId, Add = [LineData(tempId, "2", 2, "M2")] }]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_lines WHERE order_id = 2"), Is.EqualTo(2));
            h = await HistoriesAsync();
            Assert.That(h.Skip(3).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "2", "Update") }));
            Assert.That(LineNames(h[3].Snapshot), Is.EquivalentTo(new[] { "M1", "M2" }));

            //削除
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderLine", Id = "1-2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderLine", Id = "1-2" }],
            }]));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_lines WHERE order_id = 1"), Is.EqualTo(1));
            h = await HistoriesAsync();
            Assert.That(h.Skip(4).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }));
            Assert.That(LineNames(h[4].Snapshot), Is.EqualTo(new[] { "L1改" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 複合Idの行を親の画面から変える保存は今までどおり成功し版は1つ()
        {
            await AddOrderLinesAsync();
            await CreateOrderAsync("受注A");
            await _db.ExecuteAsync(Ds, "INSERT INTO order_lines (order_id, line_no, name) VALUES (1, 1, 'L1'), (1, 2, 'L2')", new());

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1",
                Update = [OrderData("1", "受注A改"), LineData("1-1", "1", 1, "L1改")],
                Add = [LineData(null, "1", 3, "L3")],
                Delete = [new ModuleDeleteInfo { ModuleName = "OrderLine", Id = "1-2" }],
            }]));
            Assert.That(await ScalarAsync("SELECT GROUP_CONCAT(name) FROM (SELECT name FROM order_lines ORDER BY line_no)"), Is.EqualTo("L1改,L3"));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }), "二重にならない");
            Assert.That(Text(h[1].Snapshot, "Title"), Is.EqualTo("受注A改"));
            Assert.That(LineNames(h[1].Snapshot), Is.EquivalentTo(new[] { "L1改", "L3" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 最新の版が今の内容とずれている親は_この保存で変わらなければ版にせず_変われば今の内容で版にする()
        {
            _design.Modules.Find("Order")!.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Items").SearchCondition.Condition = MultiMatchCondition.And(
                new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                new FieldValueMatchCondition { SearchTargetVariable = "Qty.Value", Comparison = MatchComparison.GreaterThanOrEqual, Value = MultiTypeValue.Create(1m) });
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '対象外', 0)", new());

            //履歴のインターセプタを付けない保存で親の内容を変える (最新の版 = Add とずれる)
            var plain = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            AssertNoError(await plain.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注A改")] }]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1));

            //一覧の条件に合わない行の保存: この保存では親は変わらないので版にしない
            AssertNoError(await UpdateItemAsync(ItemData("3", "1", "対象外改", 0)));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1), "最新の版とずれていても、この保存で変わっていなければ版にしない");

            //条件に合う行の保存: 版になり、内容は今の状態
            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            Assert.That(Text(h[1].Snapshot, "Title"), Is.EqualTo("受注A改"), "今の内容");
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EquivalentTo(new[] { "品X改", "品Y" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 同じ保存に親の版にならない親の送信と_その親の版に含める行の削除があると親の版が1つできる()
        {
            AddAccessories();
            HistoryOf(_design, "Order").ExcludedOwnedRecords.Add("Accessories");
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, name, qty) VALUES (1, '付属品', 0)", new());

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([
                //受注 1 の送信: 除外した付属品の行だけ (受注の版にならない)
                new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [ItemData("3", "1", "付属品改", 0)] },
                //受注 1 の版に含める明細 (品Y) の削除
                new ModuleSubmitData { ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }] },
            ]));
            Assert.That(await ScalarAsync("SELECT GROUP_CONCAT(name) FROM (SELECT name FROM order_items ORDER BY id)"), Is.EqualTo("品X,付属品改"));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => Text(e, "Name")), Is.EqualTo(new[] { "品X" }));
            Assert.That(_errors, Is.Empty);
        }

        //一時領域へ送った添付 (temp:Guid) と確定した添付 (fix:Guid) を記録する
        sealed class RecordingFiles : ITemporaryFileManager
        {
            public List<string> Calls { get; } = new();
            public Task ToTemporaryFile(string dataSourceName, Guid guid) { Calls.Add("temp:" + guid); return Task.CompletedTask; }
            public Task FixFile(string dataSourceName, Guid? guid) { Calls.Add("fix:" + guid); return Task.CompletedTask; }
            public Task<bool> IsTemporaryFileAsync(string dataSourceName, Guid guid) => Task.FromResult(true);
            public Task<Guid> CopyFileAsync(FileSaveInfo info, Guid source) => throw new NotSupportedException();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task 親の版に含まれる行のモジュールの送信で消える添付は一時領域へ送らない(bool parentHasHistory)
        {
            _design = EditHistoryTestDesigns.Create(withHistoryField: parentHasHistory);
            await _db.ExecuteAsync(Ds, "ALTER TABLE order_items ADD COLUMN file_name TEXT", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE order_items ADD COLUMN file_guid TEXT", new());
            _design.Modules.Find("OrderItem")!.Fields.Add(new FileFieldDesign { Name = "Attachment", DbColumnFileName = "file_name", DbColumnFileGuid = "file_guid" });
            var files = new RecordingFiles();
            ModuleDataIO IO()
            {
                var io = new ModuleDataIO(_design, this, _db, files);
                io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
                return io;
            }
            var (g1, g2, g3) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            await _db.ExecuteAsync(Ds, $"INSERT INTO order_items (order_id, name, qty, file_name, file_guid) VALUES (1, '品X', 1, 'a.txt', '{g1}'), (1, '品Y', 2, 'b.txt', '{g2}')", new());

            //行の削除
            AssertNoError(await IO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "1" }],
            }]));
            //行の添付の差し替え
            var item = ItemData("2", "1", "品Y", 2);
            item.Fields["Attachment"] = new FileFieldData { FileName = "c.txt", FileGuid = g3 };
            AssertNoError(await IO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = "2", Update = [item] }]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT file_guid FROM order_items WHERE id = 2", new())).Single()["file_guid"]?.ToString()?.ToLowerInvariant(), Is.EqualTo(g3.ToString()));

            var temps = files.Calls.Where(e => e.StartsWith("temp:")).ToList();
            if (parentHasHistory)
            {
                Assert.That(temps, Is.Empty, "版から戻したときに実体があるよう、消える添付を残す");
                Assert.That((await HistoriesAsync()).Select(e => e.ChangeType), Is.EqualTo(new[] { "Update", "Update" }));
            }
            else
            {
                //履歴にも親の版にも関係しない行は、本体の既定どおり一時領域へ送る
                Assert.That(temps, Is.EquivalentTo(new[] { "temp:" + g1, "temp:" + g2 }));
                Assert.That(await HistoriesAsync(), Is.Empty);
            }
            Assert.That(_errors, Is.Empty);
        }

        // ===== 別の画面で保存した行を版にしない親 (RecordRowsSavedElsewhere = false) =====

        [Test]
        public async Task 別の画面の保存を版にしない親は_行のモジュールの送信で行を更新追加削除しても版ができず保存は成功する()
        {
            HistoryOf(_design, "Order").RecordRowsSavedElsewhere = false;
            await CreateOrderAsync();

            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "OrderItem", Id = tempId, Add = [ItemData(tempId, "1", "品Z", 3)] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "OrderItem", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));

            Assert.That(await ScalarAsync("SELECT name FROM order_items WHERE id = 1"), Is.EqualTo("品X改"));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE name = '品Z'"), Is.EqualTo(1));
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE id = 2"), Is.EqualTo(0));
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add") }), "親の画面からの作成の版だけ");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 別の画面の保存を版にしない親でも_親の画面から明細を変える保存は今までどおり1版()
        {
            HistoryOf(_design, "Order").RecordRowsSavedElsewhere = false;
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1",
                Update = [ItemData("1", "1", "品X改", 5)],
                Add = [ItemData(null, "1", "品Z", 3)],
                Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Add"), ("Order", "1", "Update") }));
            Assert.That(Items(h[1].Snapshot).Select(e => (Text(e, "Name"), Number(e, "Qty"))), Is.EquivalentTo(new[] { ("品X改", (decimal?)5), ("品Z", (decimal?)3) }),
                "版の内容に明細の変更が入る");
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 自己参照で別の画面の保存を版にしないと_子の保存は自分の版だけで_上司の画面から部下と一緒に保存すると上司の版が1つ()
        {
            //1 社長 ← 2 部長 ← 3 課長
            await AddEmployeeAsync("('社長', NULL), ('部長', 1), ('課長', 2)");
            HistoryOf(_design, "Employee").RecordRowsSavedElsewhere = false;

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "3", Update = [Employee("3", "係長")] }]));
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Employee", "3", "Update") }), "祖先 (1・2) に版ができない");

            //部長 (2) の画面で、自分と部下 (3) を保存する
            var sub = Employee("3", "主任");
            sub.Fields["BossId"] = new IdFieldData { Value = "2" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "2", Update = [Employee("2", "次長"), sub] }]));
            var h = await HistoriesAsync();
            Assert.That(h.Count(e => e.DataId == "2"), Is.EqualTo(1), "上司 (画面のレコード) の版が 1 つ");
            var boss = h.Single(e => e.DataId == "2").Snapshot;
            Assert.That((Text(boss, "Name"), Text(Rows(boss, "Subordinates").Single(), "Name")), Is.EqualTo(("次長", "主任")));
            Assert.That(h.Count(e => e.DataId == "1"), Is.EqualTo(0), "その上の祖先 (1) には版ができない");
            Assert.That(_errors, Is.Empty);
        }

        //受注 1 (Order) + 見積 1 (Quote) + 両方に入る明細 1 行 (order_id = 1・quote_id = 1)
        async Task OrderAndQuoteShareItemAsync()
        {
            AddQuote(_design, withItems: true, withCustomer: false);
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (title) VALUES ('受注A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO quotes (title) VALUES ('見積A')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO order_items (order_id, quote_id, name, qty) VALUES (1, 1, '品X', 1)", new());
        }

        [Test]
        public async Task 同じ行モジュールを含む2つの親の片方が別の画面の保存を版にしなければ_設計チェック12は出ず行の保存は成功し_もう片方だけに版ができる()
        {
            await OrderAndQuoteShareItemAsync();
            HistoryOf(_design, "Quote").RecordRowsSavedElsewhere = false;
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));

            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            Assert.That(await ScalarAsync("SELECT name FROM order_items WHERE id = 1"), Is.EqualTo("品X改"));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Order", "1", "Update") }), "true の親 (Order) だけ");
            Assert.That(Text(Items(h[0].Snapshot).Single(), "Name"), Is.EqualTo("品X改"));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 同じ行モジュールを含む2つの親の両方が別の画面の保存を版にしなければ_設計チェック12は出ず行の保存でどちらにも版ができない()
        {
            await OrderAndQuoteShareItemAsync();
            HistoryOf(_design, "Order").RecordRowsSavedElsewhere = false;
            HistoryOf(_design, "Quote").RecordRowsSavedElsewhere = false;
            Assert.That(Check(_design, "Order").Select(e => e.Code), Does.Not.Contain(MultipleOwners));
            Assert.That(Check(_design, "Quote").Select(e => e.Code), Does.Not.Contain(MultipleOwners));

            AssertNoError(await UpdateItemAsync(ItemData("1", "1", "品X改", 9)));
            Assert.That(await ScalarAsync("SELECT name FROM order_items WHERE id = 1"), Is.EqualTo("品X改"));
            Assert.That(await HistoriesAsync(), Is.Empty);
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 別の画面の保存を版にしない親の行モジュールの一括追加は一括INSERTの申告を外さない()
        {
            HistoryOf(_design, "Order").RecordRowsSavedElsewhere = false;
            await CreateOrderAsync();
            var spy = new SubmitSpy();
            var io = CreateIO();
            io.AddInterceptor(spy);
            var rows = Enumerable.Range(1, 3).Select(i =>
            {
                var d = new ModuleData { Name = "OrderItem" };
                d.Fields["Order"] = new LinkFieldData { Value = "1" };
                d.Fields["Name"] = new TextFieldData { Value = $"取込{i}" };
                return d;
            }).ToList();

            var threshold = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 1;
            try
            {
                AssertNoError(await io.SubmitWithTransactionByModuleDataAsync("OrderItem", rows));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = threshold;
            }

            Assert.That(spy.Seen.Where(e => e.Module == "OrderItem").Select(e => e.NoTemporaryIdResolution), Is.EqualTo(new[] { true, true, true }),
                "親の版にならないので一括 INSERT のまま");
            Assert.That(await ScalarAsync("SELECT COUNT(*) FROM order_items WHERE name LIKE '取込%'"), Is.EqualTo(3));
            Assert.That((await HistoriesAsync()).Select(e => (e.Module, e.ChangeType)), Is.EqualTo(new[] { ("Order", "Add") }));
        }
    }
    /// <summary>
    /// タグ (TagField) の行 (ContactTags) も、親 (Contact) の版に含める従属レコード。
    /// 行から親を引く検索 ("Tags.Id.Value" In [...]) が本体で通り、親の画面からの保存は 1 版・行の単独の送信も親の版になる。
    /// </summary>
    public class EditHistoryOwnerVersionTagDbTest : IAuthenticationContext
    {
        const string Ds = TagTestDesigns.Ds;
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("7");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_owner_tag_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile};Foreign Keys=True" }]);
            await TagTestDesigns.CreateTablesAsync(_db);
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT, user_id TEXT, date_time TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, name) VALUES (7, '石川')", new());
            _design = Design();
            _errors.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        //タグのテストの設計 (Contact + ContactTags) に編集履歴を足す
        static DesignData Design()
        {
            var d = TagTestDesigns.Create();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            d.AddModule(user);
            d.Modules.Find("Contact")!.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });

            var history = new ModuleDesign { Name = "EditHistory", DataSourceName = Ds, DbTable = "edit_histories", CanCreate = false, CanUpdate = false };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new TextFieldDesign { Name = "ModuleName", DbColumn = "module_name" });
            history.Fields.Add(new TextFieldDesign { Name = "DataId", DbColumn = "data_id" });
            history.Fields.Add(new TextFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            history.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            history.Fields.Add(new TextFieldDesign { Name = "UserId", DbColumn = "user_id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "DateTime", DbColumn = "date_time" });
            history.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract" });
            history.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(history);
            return d;
        }

        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<List<(string Module, string DataId, string ChangeType, ModuleData Snapshot)>> HistoriesAsync()
            => (await _db.QueryAsync(Ds, "SELECT module_name, data_id, change_type, snapshot FROM edit_histories ORDER BY id", new()))
                .Select(e => (e["module_name"].ToString()!, e["data_id"].ToString()!, e["change_type"].ToString()!, EditHistorySnapshot.Deserialize(e["snapshot"].ToString())!)).ToList();

        static string[] TagsOf(ModuleData snapshot)
            => (snapshot.GetOwnedRows("Tags") ?? []).Select(e => ((TextFieldData)e.Fields["Name"]).Value ?? string.Empty).ToArray();

        static ModuleData TagRow(string id, string ownerId, string name)
        {
            var row = new ModuleData { Name = "ContactTags" };
            row.Fields["Id"] = new IdFieldData { Value = id };
            row.Fields["OwnerId"] = new IdFieldData { Value = ownerId };
            row.Fields["Name"] = new TextFieldData { Value = name };
            return row;
        }

        [Test]
        public async Task 親の画面からタグを足し外す保存は成功し版は1つ()
        {
            var client = new DbClientServices(_design, CreateIO);
            var module = await client.OpenAsync("Contact", "2");
            var field = (TagField)module.GetField("Tags")!;
            await field.AddTagAsync("セミナー");
            await field.RemoveTagAsync("展示会");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));

            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Contact", "2", "Update") }), "二重にならない");
            Assert.That(TagsOf(h[0].Snapshot), Is.EqualTo(new[] { "セミナー" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task タグの行を単独の送信で追加削除すると親の版になる()
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "ContactTags", Id = tempId, Add = [TagRow(tempId, "1", "新規")] }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Contact", "1", "Update") }));
            Assert.That(TagsOf(h[0].Snapshot), Is.EquivalentTo(new[] { "展示会", "DXPO", "新規" }));

            //A の DXPO (Id 2) を消す
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "ContactTags", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "ContactTags", Id = "2" }],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Contact", "1", "Update"), ("Contact", "1", "Update") }));
            Assert.That(TagsOf(h[1].Snapshot), Is.EquivalentTo(new[] { "展示会", "新規" }));
            Assert.That(_errors, Is.Empty);
        }
    }
    /// <summary>
    /// 拡張フィールド (Gantt のタスク・依存関係 / Calendar / TaskBoard / MarkerList) の子レコードも、宣言の結び付きで親の版に入る。
    /// 子レコードのモジュールの送信で行を追加・更新・削除すると、親 (Project) の版になる。
    /// </summary>
    public class EditHistoryOwnerVersionExtensionDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("7");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_owner_ext_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE projects (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE programs (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE project_tasks (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER, program_id INTEGER, title TEXT, start TEXT, end TEXT, progress REAL, status TEXT, sort_index REAL, x REAL, y REAL)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE task_dependencies (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER, source_id INTEGER, destination_id INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT, user_id TEXT, date_time TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, name) VALUES (7, '石川')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO projects (name) VALUES ('P1'), ('P2')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO project_tasks (project_id, title, start, end, progress, status, sort_index, x, y) VALUES (1, '要件定義', '2026-10-01', '2026-10-02', 0, 'Todo', 1, 1, 1), (1, '設計', '2026-10-05', '2026-10-06', 0, 'Todo', 2, 2, 2), (2, '別案件', '2026-10-01', '2026-10-02', 0, 'Todo', 1, 1, 1)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO task_dependencies (project_id, source_id, destination_id) VALUES (1, 1, 2)", new());
            _errors.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        static SearchCondition Bind(string module, string link = "Project") => new(module)
        {
            Condition = new FieldVariableMatchCondition { SearchTargetVariable = $"{link}.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
        };

        static GanttFieldDesign Gantt(string link = "Project") => new()
        {
            Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", ProgressField = "Progress", IdField = "Id",
            SearchCondition = Bind("ProjectTask", link),
            DependenciesModule = Bind("TaskDependency"),
            DependencySourceIdField = "Source",
            DependencyDestinationIdField = "Destination",
        };

        //親 (Project) の、子レコードを持つフィールドの作り方。TestCaseSource は名前を付けない (SetName は使わない)
        public static IEnumerable<Func<FieldDesignBase>> OwnerFields()
        {
            yield return () => Gantt();
            yield return () => new CalendarFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", SearchCondition = Bind("ProjectTask") };
            yield return () => new TaskBoardFieldDesign { Name = "Owner", StatusField = "Status", SortIndexField = "SortIndex", SearchCondition = Bind("ProjectTask") };
            yield return () => new MarkerListFieldDesign { Name = "Owner", XField = "X", YField = "Y", LabelField = "Title", SearchCondition = Bind("ProjectTask") };
        }

        static DesignData CreateDesign(FieldDesignBase ownerField)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            d.AddModule(user);

            var project = new ModuleDesign { Name = "Project", DataSourceName = Ds, DbTable = "projects" };
            project.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            project.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            project.Fields.Add(ownerField);
            project.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            project.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(project);

            var task = new ModuleDesign { Name = "ProjectTask", DataSourceName = Ds, DbTable = "project_tasks" };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            task.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            task.Fields.Add(new LinkFieldDesign { Name = "Program", SearchCondition = new SearchCondition("Program"), DbColumn = "program_id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Start", DbColumn = "start" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "End", DbColumn = "end" });
            task.Fields.Add(new NumberFieldDesign { Name = "Progress", DbColumn = "progress" });
            task.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "status" });
            task.Fields.Add(new NumberFieldDesign { Name = "SortIndex", DbColumn = "sort_index" });
            task.Fields.Add(new NumberFieldDesign { Name = "X", DbColumn = "x" });
            task.Fields.Add(new NumberFieldDesign { Name = "Y", DbColumn = "y" });
            task.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(task);

            var dependency = new ModuleDesign { Name = "TaskDependency", DataSourceName = Ds, DbTable = "task_dependencies" };
            dependency.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Source", SearchCondition = new SearchCondition("ProjectTask"), DbColumn = "source_id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Destination", SearchCondition = new SearchCondition("ProjectTask"), DbColumn = "destination_id" });
            dependency.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(dependency);

            var history = new ModuleDesign { Name = "EditHistory", DataSourceName = Ds, DbTable = "edit_histories", CanCreate = false, CanUpdate = false };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new TextFieldDesign { Name = "ModuleName", DbColumn = "module_name" });
            history.Fields.Add(new TextFieldDesign { Name = "DataId", DbColumn = "data_id" });
            history.Fields.Add(new TextFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            history.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            history.Fields.Add(new TextFieldDesign { Name = "UserId", DbColumn = "user_id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "DateTime", DbColumn = "date_time" });
            history.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract" });
            history.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(history);
            return d;
        }

        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<List<(string Module, string DataId, string ChangeType, ModuleData Snapshot)>> HistoriesAsync()
            => (await _db.QueryAsync(Ds, "SELECT module_name, data_id, change_type, snapshot FROM edit_histories ORDER BY id", new()))
                .Select(e => (e["module_name"].ToString()!, e["data_id"].ToString()!, e["change_type"].ToString()!, EditHistorySnapshot.Deserialize(e["snapshot"].ToString())!)).ToList();

        static string[] Titles(ModuleData snapshot)
            => (snapshot.GetOwnedRows("Owner") ?? []).Select(e => ((TextFieldData)e.Fields["Title"]).Value ?? string.Empty).ToArray();

        static ModuleData TaskData(string id, string projectId, string title)
        {
            var data = new ModuleData { Name = "ProjectTask" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Project"] = new LinkFieldData { Value = projectId };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Start"] = new DateTimeFieldData { Value = new DateTime(2026, 10, 10) };
            data.Fields["End"] = new DateTimeFieldData { Value = new DateTime(2026, 10, 11) };
            data.Fields["Progress"] = new NumberFieldData { Value = 0 };
            data.Fields["Status"] = new TextFieldData { Value = "Todo" };
            data.Fields["SortIndex"] = new NumberFieldData { Value = 9 };
            data.Fields["X"] = new NumberFieldData { Value = 9 };
            data.Fields["Y"] = new NumberFieldData { Value = 9 };
            return data;
        }

        [TestCaseSource(nameof(OwnerFields))]
        public async Task 子レコードのモジュールの送信で行を追加更新削除すると親の版になる(Func<FieldDesignBase> ownerField)
        {
            _design = CreateDesign(ownerField());
            var fieldType = _design.Modules.Find("Project")!.Fields.Single(e => e.Name == "Owner").GetType().Name;

            //更新
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "ProjectTask", Id = "2", Update = [TaskData("2", "1", "設計改")] }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Project", "1", "Update") }), fieldType);
            Assert.That(Titles(h[0].Snapshot), Is.EquivalentTo(new[] { "要件定義", "設計改" }), fieldType);

            //追加
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "ProjectTask", Id = tempId, Add = [TaskData(tempId, "1", "実装")] }]));
            h = await HistoriesAsync();
            Assert.That(h.Count, Is.EqualTo(2), fieldType);
            Assert.That(Titles(h[1].Snapshot), Is.EquivalentTo(new[] { "要件定義", "設計改", "実装" }), fieldType);

            //削除 (P2 の行: 版は P2 に)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "ProjectTask", Id = "3", Delete = [new ModuleDeleteInfo { ModuleName = "ProjectTask", Id = "3" }],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(2).Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Project", "2", "Update") }), fieldType);
            Assert.That(Titles(h[2].Snapshot), Is.Empty, fieldType);
            Assert.That(_errors, Is.Empty, fieldType);
        }

        static ModuleData DependencyData(string id, string source, string destination)
        {
            var data = new ModuleData { Name = "TaskDependency" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Project"] = new LinkFieldData { Value = "1" };
            data.Fields["Source"] = new LinkFieldData { Value = source };
            data.Fields["Destination"] = new LinkFieldData { Value = destination };
            return data;
        }

        static string[] Dependencies(ModuleData snapshot)
            => (snapshot.GetOwnedRows("Owner:Dependencies") ?? []).Select(e => $"{((LinkFieldData)e.Fields["Source"]).Value}->{((LinkFieldData)e.Fields["Destination"]).Value}").ToArray();

        [Test]
        public async Task Ganttの依存関係のモジュールの送信で行を追加更新削除すると親の版になる()
        {
            _design = CreateDesign(Gantt());
            //追加 2 → 1
            var tempId = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "TaskDependency", Id = tempId, Add = [DependencyData(tempId, "2", "1")] }]));
            //更新 1 → 2 を 2 → 2 に
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "TaskDependency", Id = "1", Update = [DependencyData("1", "2", "2")] }]));
            //削除
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "TaskDependency", Id = "2", Delete = [new ModuleDeleteInfo { ModuleName = "TaskDependency", Id = "2" }],
            }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e.Module, e.DataId, e.ChangeType)), Is.EqualTo(new[] { ("Project", "1", "Update"), ("Project", "1", "Update"), ("Project", "1", "Update") }));
            Assert.That(Dependencies(h[0].Snapshot), Is.EquivalentTo(new[] { "1->2", "2->1" }));
            Assert.That(Dependencies(h[1].Snapshot), Is.EquivalentTo(new[] { "2->2", "2->1" }));
            Assert.That(Dependencies(h[2].Snapshot), Is.EquivalentTo(new[] { "2->2" }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public void Ganttの子モジュールを2つの履歴つきの親が版に含めると設計チェック12が出る()
        {
            _design = CreateDesign(Gantt());
            var program = new ModuleDesign { Name = "Program", DataSourceName = Ds, DbTable = "programs" };
            program.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            program.Fields.Add(new GanttFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id", SearchCondition = Bind("ProjectTask", "Program") });
            program.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            program.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(program);

            var code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), 12);
            List<DesignCheckInfo> Check(string module)
                => _design.Modules.Find(module)!.Fields.OfType<EditHistoryFieldDesign>().Single().CheckDesign(new DesignCheckContext(module, _design, Utilities.CreateDataSource()));
            Assert.That(Check("Project").Count(e => e.Code == code), Is.EqualTo(1));
            Assert.That(Check("Program").Count(e => e.Code == code), Is.EqualTo(1));
            Assert.That(Check("Project").Single(e => e.Code == code).Message, Does.Contain("ProjectTask"));
        }
    }
}
