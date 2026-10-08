using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

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

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE orders (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, amount REAL, secret TEXT, is_deleted INTEGER, file_name TEXT, file_guid TEXT)", new());
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

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<List<Dictionary<string, object>>> HistoriesAsync()
            => (await _db.QueryAsync(Ds, "SELECT id, module_name, data_id, change_type, snapshot, user_id, date_time FROM edit_histories ORDER BY id", new()))
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

        //操作ユーザー (7 = 石川) には Order.Secret を読ませない
        void DenySecretForCurrentUser()
        {
            var permission = new PermissionFieldDesign { Name = "Perm" };
            permission.TargetFields.Add("Secret");
            permission.ReadCondition.ModuleName = "AppUser";
            permission.ReadCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "CurrentUser.Name.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("admin"),
            };
            _design.Modules.Find("Order")!.Fields.Add(permission);
        }

        [Test]
        public async Task スナップショットは操作ユーザーの権限に関係なく全列を記録し_返すときに読む人の権限に落とす()
        {
            DenySecretForCurrentUser();
            var tempId = "@temporary:" + Guid.NewGuid();
            //Secret は操作ユーザーには読めない列。DB に直接入れておく
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = tempId, Add = [OrderData(tempId, "受注A", 1000)] }]));
            await _db.ExecuteAsync(Ds, "UPDATE orders SET secret = '内緒' WHERE id = 1", new());
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注B", 1000)] }]));

            //DB の記録 (生) には読めない列も入っている
            var raw = Snapshot((await HistoriesAsync())[1]);
            Assert.That(((TextFieldData)raw.Fields["Secret"]).Value, Is.EqualTo("内緒"), "記録は内部読み = 操作ユーザーに読めない列も残る");

            //履歴モジュールを読むと、読む人 (7) に読めない列は落ちている
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "EditHistory" }, 0);
            var updateRow = page.Items.Single(e => ((TextFieldData)e.Fields["ChangeType"]).Value == "Update");
            var served = EditHistorySnapshot.Deserialize(((TextFieldData)updateRow.Fields["Snapshot"]).Value)!;
            Assert.That(served.Fields.ContainsKey("Secret"), Is.False, "返すときに読む人の権限で落とす");
            Assert.That(((TextFieldData)served.Fields["Title"]).Value, Is.EqualTo("受注B"));
            Assert.That(Items(served).Count, Is.EqualTo(0));
        }

        [Test]
        public async Task 行の閲覧条件に合わない版のスナップショットは空で返す()
        {
            //Title が '受注A' の行だけ読める
            _design.Modules.Find("Order")!.DataReadCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "Title.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("受注A"),
            };
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注B", 1000)] }]));
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update" }));

            //版 1 (受注A) は読める内容、版 2 (受注B) はその内容なら読めない行なので空
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "EditHistory" }, 0);
            string SnapshotOf(string changeType) => ((TextFieldData)page.Items.Single(e => ((TextFieldData)e.Fields["ChangeType"]).Value == changeType).Fields["Snapshot"]).Value ?? string.Empty;
            Assert.That(SnapshotOf("Add"), Does.Contain("受注A"));
            Assert.That(SnapshotOf("Update"), Is.Empty);
        }

        [Test]
        public async Task 対象モジュールを読めない人には履歴のスナップショットを空で返す()
        {
            var order = _design.Modules.Find("Order")!;
            order.UserReadCondition.ModuleName = "AppUser";
            order.UserReadCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "Name.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("admin"),
            };
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (id, title, amount) VALUES (1, '受注A', 1000)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO edit_histories (module_name, data_id, change_type, snapshot) VALUES ('Order', '1', 'Add', @s)",
                new() { ["s"] = EditHistorySnapshot.Serialize(OrderData("1", "受注A", 1000)) });

            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "EditHistory" }, 0);
            Assert.That(((TextFieldData)page.Items[0].Fields["Snapshot"]).Value, Is.Empty);
            Assert.That(((TextFieldData)page.Items[0].Fields["ChangeType"]).Value, Is.EqualTo("Add"), "版があることは見える");
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
        public async Task 入れなかった項目はnullとして版に残る_明細も()
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = tempId };
            order.Fields["Title"] = new TextFieldData { Value = "金額なし" };
            var item = new ModuleData { Name = "OrderItem" };
            item.Fields["Order"] = new LinkFieldData { Value = tempId };
            item.Fields["Name"] = new TextFieldData { Value = "数量なし" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = tempId, Add = [order, item] }]));

            var snapshot = Snapshot((await HistoriesAsync()).Single());
            Assert.That(snapshot.Fields.ContainsKey("Amount"), Is.True, "DB で NULL の列も項目として入る (復元で空に戻せるように)");
            Assert.That(((NumberFieldData)snapshot.Fields["Amount"]).Value, Is.Null);
            Assert.That(((TextFieldData)snapshot.Fields["Secret"]).Value, Is.Null);
            Assert.That(snapshot.Fields.ContainsKey("Id"), Is.True);
            var row = Items(snapshot).Single();
            Assert.That(((NumberFieldData)row.Fields["Qty"]).Value, Is.Null, "明細の NULL 列も同じ");
        }

        [Test]
        public async Task 一括取込のId空の新規行は作成として記録される()
        {
            //ファイル取込 / スクリプトの一括保存と同じ入口 (Id 無しの ModuleData → 投げ切りの Add)
            var a = new ModuleData { Name = "Order" };
            a.Fields["Title"] = new TextFieldData { Value = "取込A" };
            a.Fields["Amount"] = new NumberFieldData { Value = 1 };
            var b = new ModuleData { Name = "Order" };
            b.Fields["Title"] = new TextFieldData { Value = "取込B" };
            b.Fields["Amount"] = new NumberFieldData { Value = 2 };
            AssertNoError(await CreateIO().SubmitWithTransactionByModuleDataAsync("Order", [a, b]));

            var histories = await HistoriesAsync();
            Assert.That(histories.Select(h => (h["data_id"], h["change_type"])), Is.EqualTo(new[] { ("1", "Add"), ("2", "Add") }));
            Assert.That(histories.Select(h => ((TextFieldData)Snapshot(h).Fields["Title"]).Value), Is.EqualTo(new[] { "取込A", "取込B" }));
            Assert.That(_errors, Is.Empty);

            //Id 付きで取り込み直すと更新
            var a2 = OrderData("1", "取込A改", 10);
            AssertNoError(await CreateIO().SubmitWithTransactionByModuleDataAsync("Order", [a2]));
            histories = await HistoriesAsync();
            Assert.That(histories.Select(h => (h["data_id"], h["change_type"])), Is.EqualTo(new[] { ("1", "Add"), ("2", "Add"), ("1", "Update") }));
        }

        [Test]
        public async Task 一括INSERT経路の閾値内でも履歴対象モジュールは1行ずつ入り記録される()
        {
            var threshold = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 1;
            try
            {
                var rows = Enumerable.Range(1, 3).Select(i =>
                {
                    var d = new ModuleData { Name = "Order" };
                    d.Fields["Title"] = new TextFieldData { Value = $"取込{i}" };
                    d.Fields["Amount"] = new NumberFieldData { Value = i };
                    return d;
                }).ToList();
                AssertNoError(await CreateIO().SubmitWithTransactionByModuleDataAsync("Order", rows));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = threshold;
            }

            var histories = await HistoriesAsync();
            Assert.That(histories.Select(h => (h["data_id"], h["change_type"])), Is.EqualTo(new[] { ("1", "Add"), ("2", "Add"), ("3", "Add") }));
            Assert.That(_errors, Is.Empty, "一括 INSERT 経路に乗ると採番 Id が無く記録スキップのログが出る");
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
        public async Task ExecuteSqlのUpdateタイミングの保存は版になり_SQLが他のテーブルに書いたものは履歴に入らない()
        {
            //保存 (Update) のたびに別テーブルへ 1 行足す SQL
            await _db.ExecuteAsync(Ds, "CREATE TABLE side_logs (id INTEGER PRIMARY KEY AUTOINCREMENT, order_id INTEGER, memo TEXT)", new());
            var sql = new Repository.Design.ExecuteSqlFieldDesign
            {
                Name = "AfterUpdate", Timing = Repository.Design.ExecuteSqlTiming.Update, WithStandardIO = Repository.Design.ExecuteSqlWithStandardIO.After,
            };
            sql.ExecuteSqlSetting.SqlText = "INSERT INTO side_logs (order_id, memo) VALUES (1, 'updated')";
            _design.Modules.Find("Order")!.Fields.Add(sql);
            await CreateOrderAsync();

            //Update タイミングの SQL は編集中データ (CurrentEditingData = 画面の保存が送る形) からパラメータを取る
            static ModuleSubmitData Update(string title)
            {
                var editing = OrderData("1", title, 1000);
                editing.Fields["Items"] = new ListFieldData();
                editing.Fields["Related"] = new ListFieldData();
                return new() { ModuleName = "Order", Id = "1", Update = [OrderData("1", title, 1000)], CurrentEditingData = editing };
            }

            //更新 → 版 2 (保存後の内容) + 副作用 1 行
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([Update("受注B")]));
            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update" }));
            Assert.That(((TextFieldData)Snapshot(histories[1]).Fields["Title"]).Value, Is.EqualTo("受注B"));
            Assert.That(Snapshot(histories[1]).Fields.ContainsKey("AfterUpdate"), Is.False, "ExecuteSqlField はデータを持たない = スナップショットに無い");
            Assert.That((await _db.QueryAsync(Ds, "SELECT memo FROM side_logs ORDER BY id", new())).Count, Is.EqualTo(1));

            //「この版に戻す」= 版 1 の内容で保存し直す。レコードは戻り版 3 になるが、SQL が書いた他のテーブルは戻らない (その保存でも SQL が走る = 記録だけが残る)
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([Update("受注A")]));
            histories = await HistoriesAsync();
            Assert.That(histories.Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update", "Update" }));
            Assert.That(((TextFieldData)Snapshot(histories[2]).Fields["Title"]).Value, Is.EqualTo("受注A"));
            Assert.That((await _db.QueryAsync(Ds, "SELECT memo FROM side_logs ORDER BY id", new())).Count, Is.EqualTo(2), "副作用は消えず、戻す保存の分が増える");
            Assert.That(_errors, Is.Empty);
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

            //復活ボタンが送る形: Add / Update 無し、ExtendedData に「削除の版の履歴行」だけ (戻す Id はサーバーがスナップショットから組む)
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            Assert.That(results[0].DestinationId, Is.EqualTo("1"), "論理削除は同じ Id");

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

            //「この版に戻す」→ 保存が送る形: 復活した行の Update + ExtendedData に「戻した版の履歴行」(その版の論理削除の行をサーバーが取り消す)
            var addRow = (await HistoriesAsync())[0];
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [ItemData("2", "1", "品Y", 7)] };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = addRow["id"]!.ToString()! });
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([submit]));

            var items = Items(Snapshot((await HistoriesAsync())[2]));
            Assert.That(items.Select(e => (EditHistorySnapshot.GetId(e), ((NumberFieldData)e.Fields["Qty"]).Value)),
                Is.EquivalentTo(new[] { ("1", (decimal?)1), ("2", (decimal?)7) }), "Id 2 のまま戻り、値も更新されている");
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Update", "Update" }));
        }

        [Test]
        public async Task 物理削除したレコードの復活はスナップショットから作り直し_明細も新しい親のIdで作り直す()
        {
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(0));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM order_items", new())).Single()["c"], Is.EqualTo(0), "明細も物理削除される");

            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            var newId = results[0].DestinationId;
            Assert.That(newId, Is.Not.EqualTo("1").And.Not.Empty, "Id は振り直し");

            var orders = await _db.QueryAsync(Ds, "SELECT id, title, amount FROM orders", new());
            Assert.That(orders.Select(e => (e["id"]!.ToString(), e["title"])), Is.EqualTo(new[] { (newId, (object)"受注A") }));
            var items = await _db.QueryAsync(Ds, "SELECT order_id, name FROM order_items ORDER BY id", new());
            Assert.That(items.Select(e => (e["order_id"]!.ToString(), e["name"])), Is.EquivalentTo(new[] { (newId, (object)"品X"), (newId, (object)"品Y") }), "明細は新しい親の Id で作り直す");

            var histories = await HistoriesAsync();
            Assert.That(histories.Select(e => (e["change_type"], e["data_id"])), Is.EqualTo(new[] { ("Add", newId), ("Delete", newId), ("Restore", newId) }),
                "旧 Id の版は作り直した Id に付け替わり (履歴が繋がる)、復活の版が積まれる");
            Assert.That(Items(Snapshot(histories[2])).Count, Is.EqualTo(2));

            //同じ削除の版からもう一度は復活できない (最新の版が削除ではない)
            var again = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(again.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "復活済みのレコードの削除の版からは復活できない");
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(1), "二重に作られない");
        }

        [Test]
        public async Task 復元を禁止した履歴フィールドのモジュールは復活できない()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            _design.Modules.Find("Order")!.Fields.OfType<Extras.Designs.EditHistoryFieldDesign>().Single().CanRestore = false;
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders WHERE is_deleted = 1", new())).Single()["c"], Is.EqualTo(1), "削除のまま");
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Delete" }));
        }

        [Test]
        public async Task 論理削除の復活も最新の版が削除のときだけ()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([submit]));

            //復活の版が後ろに積まれた = この削除の版は最新ではない
            var again = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(again.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Delete", "Restore" }), "余分な復活の版は積まれない");
        }

        [Test]
        public async Task 手入力Idのレコードは元のIdで作り直し_同じIdがあれば復活できない()
        {
            _design = EditHistoryTestDesigns.Create(manualId: true);
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "100", Add = [OrderData("100", "受注A", 1000), ItemData(null, "100", "品X", 1)],
            }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "100", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "100" }],
            }]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(0));

            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "100" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            Assert.That(results[0].DestinationId, Is.EqualTo("100"), "手入力 Id は元の Id");
            Assert.That((await _db.QueryAsync(Ds, "SELECT id FROM orders", new())).Single()["id"]!.ToString(), Is.EqualTo("100"));
            Assert.That((await _db.QueryAsync(Ds, "SELECT order_id FROM order_items", new())).Single()["order_id"]!.ToString(), Is.EqualTo("100"), "明細の参照も元の Id");
            Assert.That((await HistoriesAsync()).Select(e => (e["change_type"], e["data_id"])), Is.EqualTo(new[] { ("Add", "100"), ("Delete", "100"), ("Restore", "100") }));

            //消した後に同じ Id で別のレコードが作られていたら復活できない
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "100", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "100" }],
            }]));
            await _db.ExecuteAsync(Ds, "INSERT INTO orders (id, title) VALUES (100, '別のレコード')", new());
            var deleteRow2 = (await HistoriesAsync()).Last(e => (string)e["change_type"] == "Delete");
            submit.ExtendedData.Clear();
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow2["id"]!.ToString()!, RestoreWholeRecord = true });
            var conflict = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(conflict.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That((await _db.QueryAsync(Ds, "SELECT title FROM orders", new())).Single()["title"], Is.EqualTo("別のレコード"), "既存のレコードは触らない");
        }

        [Test]
        public async Task 履歴モジュールへの追加と更新はユーザーからはできない()
        {
            await CreateOrderAsync();
            var row = (await HistoriesAsync())[0];
            var fake = new ModuleData { Name = "EditHistory" };
            fake.Fields["Id"] = new IdFieldData { Value = row["id"]!.ToString() };
            fake.Fields["Snapshot"] = new TextFieldData { Value = "{}" };
            var update = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "EditHistory", Id = fake.Fields["Id"].ToString()!, Update = [fake] }]);
            Assert.That(update.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "版の書き換えは拒否");
            var added = new ModuleData { Name = "EditHistory" };
            added.Fields["ModuleName"] = new TextFieldData { Value = "Order" };
            var add = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "EditHistory", Id = "@temporary:x", Add = [added] }]);
            Assert.That(add.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "版の追加は拒否");
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(1));
            //削除 (古い版の整理) はできる
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "EditHistory", Id = row["id"]!.ToString()!, Delete = [new ModuleDeleteInfo { ModuleName = "EditHistory", Id = row["id"]!.ToString()! }],
            }]));
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(0));
        }

        [Test]
        public async Task 別のモジュールの送信に混ぜた履歴モジュールへの追加と更新も拒否する()
        {
            await CreateOrderAsync();
            //デザインチェックを無視して履歴モジュールを書ける設定にしていても、版はシステムだけが書く
            var history = _design.Modules.Find("EditHistory")!;
            history.CanCreate = true;
            history.CanUpdate = true;
            var row = (await HistoriesAsync())[0];

            var added = new ModuleData { Name = "EditHistory" };
            added.Fields["ModuleName"] = new TextFieldData { Value = "Order" };
            var add = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注B", 1000)], Add = [added] }]);
            Assert.That(add.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "版の追加は拒否");

            var fake = new ModuleData { Name = "EditHistory" };
            fake.Fields["Id"] = new IdFieldData { Value = row["id"]!.ToString() };
            fake.Fields["Snapshot"] = new TextFieldData { Value = "{}" };
            var update = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注B", 1000), fake] }]);
            Assert.That(update.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "版の書き換えは拒否");

            var histories = await HistoriesAsync();
            Assert.That(histories.Count, Is.EqualTo(1));
            Assert.That(histories[0]["snapshot"], Is.EqualTo(row["snapshot"]));
        }

        [Test]
        public async Task スナップショットを検索条件や並びに使う読み出しは拒否する()
        {
            DenySecretForCurrentUser();
            await CreateOrderAsync();
            await _db.ExecuteAsync(Ds, "UPDATE orders SET secret = '内緒' WHERE id = 1", new());
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注B", 1000)] }]));

            //生のスナップショットで絞ると、返る行の有無から読めない列の値を推測できる
            var byValue = new SearchCondition
            {
                ModuleName = "EditHistory",
                Condition = new FieldValueMatchCondition { SearchTargetVariable = "Snapshot.Value", Comparison = MatchComparison.Like, Value = MultiTypeValue.Create("内緒") },
            };
            Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateIO().GetListAsync(byValue, 0));

            var bySort = new SearchCondition { ModuleName = "EditHistory", SortConditions = [new SortCondition { Variable = "Snapshot.Value" }] };
            Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateIO().GetListAsync(bySort, 0));

            //他の項目での検索はできる
            var byType = new SearchCondition
            {
                ModuleName = "EditHistory",
                Condition = new FieldValueMatchCondition { SearchTargetVariable = "ChangeType.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("Update") },
            };
            Assert.That((await CreateIO().GetListAsync(byType, 0)).Items.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task 手入力Idを変更した保存は旧Idの版を新しいIdに付け替える()
        {
            _design = EditHistoryTestDesigns.Create(manualId: true);
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "100", Add = [OrderData("100", "受注A", 1000)] }]));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "100", Update = [OrderData("100", "受注B", 1000)] }]));

            var results = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "100", Update = [OrderData("@replace:100->200", "受注C", 1000)] }]);
            AssertNoError(results);
            Assert.That(results[0].DestinationId, Is.EqualTo("200"));
            Assert.That((await _db.QueryAsync(Ds, "SELECT id FROM orders", new())).Single()["id"]!.ToString(), Is.EqualTo("200"));
            Assert.That((await HistoriesAsync()).Select(e => (e["change_type"], e["data_id"])),
                Is.EqualTo(new[] { ("Add", "200"), ("Update", "200"), ("Update", "200") }), "履歴が新しい Id で繋がる");
        }

        [Test]
        public async Task この版に戻すで戻せない行があれば保存全体を失敗にする()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            //明細を削除できない人 (= 復活もできない) が戻す
            _design.Modules.Find("OrderItem")!.CanDelete = false;
            var addRow = (await HistoriesAsync())[0];
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [ItemData("2", "1", "品Y", 7)] };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = addRow["id"]!.ToString()! });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That((await _db.QueryAsync(Ds, "SELECT is_deleted FROM order_items WHERE id = 2", new())).Single()["is_deleted"], Is.EqualTo(1), "部分反映しない");
            Assert.That((await HistoriesAsync()).Count, Is.EqualTo(2), "版も増えない");
        }

        [Test]
        public async Task この版に戻すはその人に見えない行と削除されていない行を触らない()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            //品X の行しか見えない人が版 1 に戻す (見えない 品Y は差分に出ないので触らない。見えている 品X は削除されていないので触らない)
            _design.Modules.Find("OrderItem")!.DataReadCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "Name.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("品X"),
            };
            var before = (await _db.QueryAsync(Ds, "SELECT is_deleted FROM order_items ORDER BY id", new())).Select(e => e["is_deleted"]).ToList();
            var addRow = (await HistoriesAsync())[0];
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = [OrderData("1", "受注A", 1000)] };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = addRow["id"]!.ToString()! });
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([submit]));
            var after = (await _db.QueryAsync(Ds, "SELECT is_deleted FROM order_items ORDER BY id", new())).Select(e => e["is_deleted"]).ToList();
            Assert.That(after, Is.EqualTo(before), "見えない行 (2) は削除のまま、見えている行 (1) は元のまま");
        }

        [Test]
        public async Task 論理削除の親の復活は親と一緒に消えた行を子の権限に関係なく戻す()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            //明細の行の書き込み条件がどの行にも合わない状態でも (親の削除が子を消すのと同じ規則で) 親と一緒に戻る
            _design.Modules.Find("OrderItem")!.DataWriteCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "Name.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("無い名前"),
            };
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([submit]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM order_items WHERE is_deleted = 1", new())).Single()["c"], Is.EqualTo(0));
        }

        [Test]
        public async Task 別のレコードの版を指定した復活は保存を失敗にする()
        {
            await CreateOrderAsync();
            var addRow = (await HistoriesAsync())[0];
            //Id 2 の保存に Id 1 の版を指定する (改ざん)
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "2", Update = [OrderData("2", "X", 1)] };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = addRow["id"]!.ToString()! });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
        }

        [Test]
        public async Task 何も保存しない送信は版にしない()
        {
            await CreateOrderAsync();
            //承認の申請で申請書に変更が無いときなど: Add / Update / Delete の無い送信 (base は何も書かない)
            var results = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = "1" }]);
            AssertNoError(results);
            Assert.That(results.Single().DestinationId, Is.EqualTo("1"));
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add" }), "版は増えない");
            Assert.That(_errors, Is.Empty);
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

        //変更日時は本体の CreatedAt と同じ決め方 (DateTime 役割のフィールドの SaveAsUtc)。サーバーローカル固定ではない。
        //このマシンの時差が 0 だと UTC とローカルの区別が付かないので、そのときは片方の検証だけ意味を持つ
        [Test]
        public async Task 変更日時はSaveAsUtcが無ければサーバーローカル()
        {
            var before = DateTime.Now;
            await CreateOrderAsync();
            var recorded = RecordedDateTime((await HistoriesAsync()).Single());
            AssertWithin(recorded, before, DateTime.Now);
            if (TimeZoneInfo.Local.BaseUtcOffset != TimeSpan.Zero)
                Assert.That(Math.Abs((recorded - DateTime.UtcNow).TotalMinutes), Is.GreaterThan(1), "UTC ではない");
        }

        [Test]
        public async Task 変更日時はSaveAsUtcならUTC()
        {
            _design.Modules.Find("EditHistory")!.Fields.OfType<DateTimeFieldDesign>().Single(e => e.Name == "DateTime").SaveAsUtc = true;
            var before = DateTime.UtcNow;
            await CreateOrderAsync();
            var recorded = RecordedDateTime((await HistoriesAsync()).Single());
            AssertWithin(recorded, before, DateTime.UtcNow);
            if (TimeZoneInfo.Local.BaseUtcOffset != TimeSpan.Zero)
                Assert.That(Math.Abs((recorded - DateTime.Now).TotalMinutes), Is.GreaterThan(1), "サーバーローカルではない");
        }

        static DateTime RecordedDateTime(Dictionary<string, object> history)
            => DateTime.SpecifyKind(DateTime.Parse(history["date_time"].ToString()!), DateTimeKind.Unspecified);

        //ミリ秒まで丸めるので before より僅かに前になり得る → 1 秒の余裕
        static void AssertWithin(DateTime recorded, DateTime before, DateTime after)
            => Assert.That(recorded, Is.InRange(before.AddSeconds(-1), after.AddSeconds(1)), $"recorded={recorded:O} before={before:O} after={after:O}");
        [Test]
        public async Task 復活は今そのレコードが無いときだけ_論理削除()
        {
            _design = EditHistoryTestDesigns.Create(logicalDelete: true);
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            //履歴を通らない経路で戻っていた (削除されていない) レコードには復活の版を積まない
            await _db.ExecuteAsync(Ds, "UPDATE orders SET is_deleted = 0 WHERE id = 1", new());
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(results[0].ExceptionMessage, Does.Contain("exists"));
            Assert.That((await HistoriesAsync()).Select(e => e["change_type"]), Is.EqualTo(new[] { "Add", "Delete" }), "復活の版は積まれない");
        }

        [Test]
        public async Task 復活は今そのレコードが無いときだけ_物理削除は復活の版を消しても二重に作らない()
        {
            await CreateOrderAsync();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            var newId = results[0].DestinationId;

            //復活の版を消すと削除の版 (新しい Id に付け替え済み) が最新に戻る (古い版の削除は許している) が、レコードは存在するので作り直さない
            await _db.ExecuteAsync(Ds, "DELETE FROM edit_histories WHERE change_type = 'Restore'", new());
            deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            Assert.That(deleteRow["data_id"], Is.EqualTo(newId));
            var again = new ModuleSubmitData { ModuleName = "Order", Id = newId };
            again.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            results = await CreateIO().SubmitWithTransactionAsync([again]);
            Assert.That(results.All(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(results[0].ExceptionMessage, Does.Contain("exists"));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM orders", new())).Single()["c"], Is.EqualTo(1), "二重に作られない");
        }

        class RecordingFiles : ITemporaryFileManager
        {
            public List<string> Calls { get; } = new();
            public Task ToTemporaryFile(string dataSourceName, Guid guid) { Calls.Add("temp:" + guid); return Task.CompletedTask; }
            public Task FixFile(string dataSourceName, Guid? guid) { Calls.Add("fix:" + guid); return Task.CompletedTask; }
        }

        [Test]
        public async Task 履歴を持つモジュールの削除では添付ファイルを一時領域へ送らず_復活で同じ添付を参照する()
        {
            _design = EditHistoryTestDesigns.Create(withFile: true);
            var files = new RecordingFiles();
            ModuleDataIO IO()
            {
                var io = new ModuleDataIO(_design, this, _db, files);
                io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
                return io;
            }
            var guid = Guid.NewGuid();
            var tempId = "@temporary:" + Guid.NewGuid();
            var order = OrderData(tempId, "受注A", 1000);
            order.Fields["Attachment"] = new FileFieldData { FileName = "a.txt", FileGuid = guid };
            AssertNoError(await IO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Order", Id = tempId, Add = [order] }]));

            //削除: 本体は添付を一時領域へ移す (期限後に消える) が、履歴を持つモジュールでは残す
            AssertNoError(await IO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            Assert.That(files.Calls, Is.Empty, "添付を一時領域へ送らない");

            //復活: 作り直したレコードが同じ添付を参照する (実体が残っているので開ける)
            var deleteRow = (await HistoriesAsync()).Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await IO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            var restored = (await _db.QueryAsync(Ds, $"SELECT file_name, file_guid FROM orders WHERE id = {results[0].DestinationId}", new())).Single();
            Assert.That((restored["file_name"], restored["file_guid"]?.ToString()?.ToLowerInvariant()), Is.EqualTo(((object)"a.txt", guid.ToString())));

            //履歴を持たないモジュールでは従来どおり一時領域へ送られる
            _design = EditHistoryTestDesigns.Create(withHistoryField: false, withFile: true);
            var guid2 = Guid.NewGuid();
            await _db.ExecuteAsync(Ds, $"INSERT INTO orders (id, title, file_name, file_guid) VALUES (10, 'x', 'b.txt', '{guid2}')", new());
            AssertNoError(await IO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "10", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "10" }],
            }]));
            Assert.That(files.Calls, Is.EqualTo(new[] { "temp:" + guid2 }));
        }

        [Test]
        public async Task 履歴を持たない親経由で編集した子の行は子の履歴に行ごとに記録され_親の削除でも削除の版が残る()
        {
            _design = EditHistoryTestDesigns.Create(withHistoryField: false, itemHistory: true);
            var tempId = "@temporary:" + Guid.NewGuid();
            var t1 = "@temporary:" + Guid.NewGuid();
            var t2 = "@temporary:" + Guid.NewGuid();
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempId,
                Add = [OrderData(tempId, "受注A", 1000), ItemData(t1, tempId, "品X", 1), ItemData(t2, tempId, "品Y", 2)],
            }]));
            var h = await HistoriesAsync();
            Assert.That(h.Select(e => (e["module_name"], e["change_type"], e["data_id"])),
                Is.EqualTo(new[] { ("OrderItem", "Add", "1"), ("OrderItem", "Add", "2") }), "親には版が無く、子は自分の履歴に行ごと (採番 Id)");
            Assert.That(((TextFieldData)Snapshot(h[0]).Fields["Name"]).Value, Is.EqualTo("品X"));

            //親経由の行の更新と削除
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1",
                Update = [ItemData("1", "1", "品X改", 3)],
                Delete = [new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "2" }],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(2).Select(e => (e["module_name"], e["change_type"], e["data_id"])),
                Is.EqualTo(new[] { ("OrderItem", "Update", "1"), ("OrderItem", "Delete", "2") }));
            Assert.That(((TextFieldData)Snapshot(h[3]).Fields["Name"]).Value, Is.EqualTo("品Y"), "削除の版は削除前の内容");

            //親の削除で一緒に消える行にも削除の版
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Order", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Order", Id = "1" }],
            }]));
            h = await HistoriesAsync();
            Assert.That(h.Skip(4).Select(e => (e["module_name"], e["change_type"], e["data_id"])), Is.EqualTo(new[] { ("OrderItem", "Delete", "1") }));
            Assert.That(h.Any(e => (string)e["module_name"] == "Order"), Is.False, "親の版は無い");
        }

    }
}
