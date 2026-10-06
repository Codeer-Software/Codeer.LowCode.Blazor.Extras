using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// ModuleDataAccessToolSet / ModuleDataAccessAgent: 実行ユーザーの ModuleDataIO を通るので、モジュールの閲覧権限・行の条件・項目の読み取り権限が画面と同じに効くことを実 DB (SQLite) で検証する。
    /// ユーザー: "1"=一般 (Rank 1。Order は読めない) / "2"=担当 A (Rank 10) / "3"=担当 B (Rank 10) / "9"=管理者 (Rank 30)
    /// (SQLite の日付列は CLB の規約どおり DATE 型。TEXT 型だと日付の比較は文字列比較になる)
    /// モジュール Order (Customer へのリンクと、リンク越しフィールド Customer.Name を持つ): UserReadCondition = Rank &gt;= 5、DataReadCondition = OwnerId == CurrentUser.Id (自分の行だけ)、
    ///   PermissionField で Amount は Rank &gt;= 20 の人にだけ見せる。Status は候補値つき SelectField、Customer は Customer モジュールへのリンク。
    /// </summary>
    public class ModuleDataAccessToolSetDbTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _currentUserId = "9";
        DesignData _designData = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult(_currentUserId);

        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) { }
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"moduledataaccess_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });

            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Rank INTEGER, IsActive INTEGER, Name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1', 1, 1, '一般'), ('2', 10, 1, '担当A'), ('3', 10, 1, '担当B'), ('9', 30, 1, '管理者')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Customers VALUES (1, '青空商事'), (2, '緑山工業')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Title TEXT, Amount NUMERIC, Status TEXT, OrderedOn DATE, CustomerId INTEGER, OwnerId TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Orders VALUES " +
                "(1, '机', 100, '10', '2026-01-10', 1, '2'), " +
                "(2, '椅子', 50, '20', '2026-01-20', 1, '2'), " +
                "(3, '棚', 300, '20', '2026-02-05', 2, '2'), " +
                "(4, '照明', 80, '10', '2026-02-15', 2, '3')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER, Item TEXT, Qty INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO OrderLines VALUES (1, 1, '天板', 1), (2, 1, '脚', 4), (3, 3, '棚板', 3)", new());

            _designData = CreateDesignData();
            _currentUserId = "9";
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_dbFile); } catch { }
        }

        #region ハーネス

        static FieldValueMatchCondition Match(string variable, MatchComparison comparison, MultiTypeValue value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = comparison,
            Value = value,
        };

        DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            d.AppSettings.AppAccessConditions.ModuleName = "AppUser";
            d.AppSettings.AppAccessConditions.Condition = Match("IsActive.Value", MatchComparison.Equal, MultiTypeValue.Create(true));

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new NumberFieldDesign { Name = "Rank", DbColumn = "Rank" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsActive", DbColumn = "IsActive" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            d.AddModule(user);

            var customer = new ModuleDesign { Name = "Customer", DataSourceName = Ds, DbTable = "Customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            d.AddModule(customer);

            var order = new ModuleDesign { Name = "Order", DataSourceName = Ds, DbTable = "Orders" };
            order.UserReadCondition.ModuleName = "AppUser";
            order.UserReadCondition.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            //自分が担当の行だけ (管理者も同じ条件 = 行の条件が確かに効いていることを見る)
            order.DataReadCondition.ModuleName = "Order";
            order.DataReadCondition.Condition = new FieldVariableMatchCondition { SearchTargetVariable = "OwnerId.Value", Comparison = MatchComparison.Equal, Variable = "CurrentUser.Id.Value" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            order.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DbColumn = "Amount" });
            var status = new SelectFieldDesign { Name = "Status", DbColumn = "Status" };
            status.Candidates.AddRange(new[] { "進行中,10", "完了,20" });
            order.Fields.Add(status);
            order.Fields.Add(new DateFieldDesign { Name = "OrderedOn", DbColumn = "OrderedOn" });
            var link = new LinkFieldDesign { Name = "Customer", DbColumn = "CustomerId", ValueVariable = "Id.Value", DisplayTextVariable = "Name.Value" };
            link.SearchCondition.ModuleName = "Customer";
            order.Fields.Add(link);
            //リンク越しフィールド (デザイナでリンク先の項目をレイアウトに置くと作られる列)。これがあるとリンクの表示名と、リンク先の項目での絞り込みが使える
            order.Fields.Add(new TextFieldDesign { Name = "Customer.Name", DbColumn = "Name" });
            order.Fields.Add(new TextFieldDesign { Name = "OwnerId", DbColumn = "OwnerId" });
            //明細 (Order の子一覧)。get_record で親と一緒に読む
            order.Fields.Add(new ListFieldDesign
            {
                Name = "Lines",
                SearchCondition = new SearchCondition("OrderLine")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                    SortConditions = { new SortCondition { Variable = "Id.Value" } },
                },
            });
            //件数上限つき (ページングで読む) 一覧。本体は親と一緒に読まない
            order.Fields.Add(new ListFieldDesign
            {
                Name = "PagedLines",
                SearchCondition = new SearchCondition("OrderLine")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                    LimitCount = 10,
                },
            });
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "Amount" } };
            permission.ReadCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            order.Fields.Add(permission);
            d.AddModule(order);

            var line = new ModuleDesign { Name = "OrderLine", DataSourceName = Ds, DbTable = "OrderLines" };
            line.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            var orderLink = new LinkFieldDesign { Name = "Order", DbColumn = "OrderId", ValueVariable = "Id.Value", DisplayTextVariable = "Title.Value" };
            orderLink.SearchCondition.ModuleName = "Order";
            line.Fields.Add(orderLink);
            line.Fields.Add(new TextFieldDesign { Name = "Item", DbColumn = "Item" });
            line.Fields.Add(new NumberFieldDesign { Name = "Qty", DbColumn = "Qty" });
            //子一覧として読まれる項目は一覧レイアウトに置いた項目 (画面と同じ)
            line.ListLayouts[""] = new ListLayoutDesign { Elements = [new List<ListElement> { new() { FieldName = "Item" }, new() { FieldName = "Qty" } }] };
            d.AddModule(line);
            return d;
        }

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す (ホストの DataService(userId) に相当)
        Task<ModuleDataAccessScope> OpenScope(string userId)
        {
            _currentUserId = userId;
            var io = new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            return Task.FromResult(new ModuleDataAccessScope(io, null, _db));
        }

        ModuleDataAccessToolSet Create(Action<ModuleDataAccessOptions>? configure = null)
        {
            var options = new ModuleDataAccessOptions();
            configure?.Invoke(options);
            return new ModuleDataAccessToolSet(OpenScope, () => _designData, options);
        }

        static AIChatToolContext Context(string user, Progress? progress = null)
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = user }, progress ?? new Progress(), CancellationToken.None, null);

        static async Task<JsonElement> InvokeAsync(IEnumerable<AITool> tools, string name, Dictionary<string, object?> args)
        {
            var function = tools.OfType<AIFunction>().Single(t => t.Name == name);
            var result = await function.InvokeAsync(new AIFunctionArguments(args));
            var json = result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        static object Filter(string field, string comparison, object? value) => new { Field = field, Comparison = comparison, Value = value };

        static string? ErrorOf(JsonElement e) => e.TryGetProperty("error", out var error) ? error.GetString() : null;

        #endregion

        [Test]
        public async Task find_recordsは行の条件で絞られ選択とリンクは値と表示名で返る()
        {
            var progress = new Progress();
            var tools = Create().CreateTools(Context("2", progress)).ToList();

            var result = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "担当の受注を一覧",
                ["sort"] = new[] { new { Field = "OrderedOn", Descending = true } },
            });

            //担当 A の 3 行だけ (担当 B の照明は行の条件で出ない)
            Assert.That(result.GetProperty("totalCount").GetInt32(), Is.EqualTo(3));
            var rows = result.GetProperty("rows").EnumerateArray().ToList();
            Assert.That(rows.Select(r => r.GetProperty("Title").GetString()), Is.EqualTo(new[] { "棚", "椅子", "机" }));
            //選択は候補値の表示名、リンクは相手の表示項目
            Assert.That(rows[0].GetProperty("Status").GetProperty("value").GetString(), Is.EqualTo("20"));
            Assert.That(rows[0].GetProperty("Status").GetProperty("text").GetString(), Is.EqualTo("完了"));
            Assert.That(rows[0].GetProperty("Customer").GetProperty("text").GetString(), Is.EqualTo("緑山工業"));
            Assert.That(rows[0].GetProperty("OrderedOn").GetString(), Is.EqualTo("2026-02-05"));
            //Rank 10 には Amount の読み取り権限が無い = 項目ごと出ない
            Assert.That(rows[0].TryGetProperty("Amount", out _), Is.False);
            Assert.That(progress.Texts, Does.Contain("担当の受注を一覧"));
        }

        [Test]
        public async Task 読み取り権限のある人にはAmountが見える()
        {
            //管理者 (Rank 30) は Amount を読めるが、行の条件 (自分が担当) で 1 行も無い → 項目の有無は担当 A で Rank を上げて見る
            await _db.ExecuteAsync(Ds, "UPDATE AppUsers SET Rank = 30 WHERE Id = '2'", new());
            var tools = Create().CreateTools(Context("2")).ToList();

            var result = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new[] { Filter("Title", "Equal", "机") },
            });

            var row = result.GetProperty("rows").EnumerateArray().Single();
            Assert.That(row.GetProperty("Amount").GetDecimal(), Is.EqualTo(100m));
        }

        [Test]
        public async Task 閲覧権限の無いモジュールはaccessDeniedのエラーになる()
        {
            var tools = Create().CreateTools(Context("1")).ToList();

            var result = await InvokeAsync(tools, "find_records", new() { ["moduleName"] = "Order", ["purpose"] = "p" });

            Assert.That(result.TryGetProperty("error", out var error), Is.True);
            Assert.That(error.GetString(), Is.Not.Empty);
            Assert.That(result.GetProperty("accessDenied").GetBoolean(), Is.True);
        }

        [Test]
        public async Task 条件は項目の型に合わせて変換されInとLikeとリンク先の項目が使える()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            //候補値 (文字列のコード) の In
            var byStatus = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new[] { Filter("Status", "In", new[] { "20" }) },
                ["fields"] = new[] { "Title" },
            });
            Assert.That(byStatus.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EquivalentTo(new[] { "椅子", "棚" }));
            //fields で絞っても Id は返る
            Assert.That(byStatus.GetProperty("rows").EnumerateArray().First().TryGetProperty("Id", out _), Is.True);

            //日付の比較 (文字列 → DateOnly) と記号の比較名
            var byDate = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new[] { Filter("OrderedOn", ">=", "2026-02-01") },
            });
            Assert.That(byDate.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EqualTo(new[] { "棚" }));

            //部分一致と、リンク先の項目での絞り込み (OR)
            var any = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new[] { Filter("Title", "Like", "子"), Filter("Customer.Name", "Equal", "緑山工業") },
                ["matchAny"] = true,
            });
            Assert.That(any.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EquivalentTo(new[] { "椅子", "棚" }));
        }

        [Test]
        public async Task 条件はグループで入れ子にでき否定もできる()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            //机 (Title) OR (完了 AND 2 月以降 = 棚)。椅子は完了だが 1 月なので外れる
            var nested = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new object[]
                {
                    new
                    {
                        Any = new object[]
                        {
                            Filter("Title", "Like", "机"),
                            new { All = new object[] { Filter("Status", "Equal", "20"), Filter("OrderedOn", ">=", "2026-02-01") } },
                        },
                    },
                },
            });
            Assert.That(nested.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EquivalentTo(new[] { "机", "棚" }));

            //否定: 完了でない = 机
            var not = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new object[] { new { Field = "Status", Comparison = "Equal", Value = "20", Not = true } },
            });
            Assert.That(not.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EqualTo(new[] { "机" }));

            //グループの否定: NOT (完了 AND 2 月以降) = 机・椅子
            var notGroup = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new object[] { new { All = new object[] { Filter("Status", "Equal", "20"), Filter("OrderedOn", ">=", "2026-02-01") }, Not = true } },
            });
            Assert.That(notGroup.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EquivalentTo(new[] { "机", "椅子" }));

            //all と any の同時指定はエラー
            var both = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new object[] { new { All = new object[] { Filter("Title", "Equal", "机") }, Any = new object[] { Filter("Title", "Equal", "棚") } } },
            });
            Assert.That(both.GetProperty("error").GetString(), Does.Contain("all"));
        }

        [Test]
        public async Task 知らない項目や比較はエラーメッセージで返しモジュールが無ければ案内する()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            var unknownField = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new[] { Filter("Price", "Equal", 1) },
            });
            Assert.That(unknownField.GetProperty("error").GetString(), Does.Contain("Price"));

            var unknownComparison = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["filters"] = new[] { Filter("Title", "Between", 1) },
            });
            Assert.That(unknownComparison.GetProperty("error").GetString(), Does.Contain("Between"));

            var unknownModule = await InvokeAsync(tools, "find_records", new() { ["moduleName"] = "Nothing", ["purpose"] = "p" });
            Assert.That(unknownModule.GetProperty("error").GetString(), Does.Contain("list_modules"));
        }

        [Test]
        public async Task limitは上限で頭打ちになりページで続きが取れる()
        {
            var tools = Create(o => o.MaxRows = 2).CreateTools(Context("2")).ToList();

            var first = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["sort"] = new[] { new { Field = "Id", Descending = false } },
                ["limit"] = 100,
            });
            Assert.That(first.GetProperty("rowCount").GetInt32(), Is.EqualTo(2));
            Assert.That(first.GetProperty("totalCount").GetInt32(), Is.EqualTo(3));
            Assert.That(first.GetProperty("pageCount").GetInt32(), Is.EqualTo(2));

            var second = await InvokeAsync(tools, "find_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["sort"] = new[] { new { Field = "Id", Descending = false } },
                ["limit"] = 100,
                ["page"] = 1,
            });
            Assert.That(second.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("Title").GetString()), Is.EqualTo(new[] { "棚" }));
        }

        [Test]
        public async Task aggregate_recordsは権限の範囲でサーバーが数えて足す()
        {
            await _db.ExecuteAsync(Ds, "UPDATE AppUsers SET Rank = 30 WHERE Id = '2'", new());
            var progress = new Progress();
            var tools = Create().CreateTools(Context("2", progress)).ToList();

            var result = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "状態別の金額",
                ["measures"] = new object[] { new { Function = "sum", Field = "Amount" }, new { Function = "count" } },
                ["groupBy"] = new[] { new { Field = "Status" } },
                ["sort"] = new object[] { new { Target = "measure", Index = 0, Descending = true } },
            });

            Assert.That(result.GetProperty("totalCount").GetInt32(), Is.EqualTo(3), "担当 B の行は数えない");
            Assert.That(result.GetProperty("groupCount").GetInt32(), Is.EqualTo(2));
            Assert.That(result.GetProperty("limited").GetBoolean(), Is.False);
            var groups = result.GetProperty("groups").EnumerateArray().ToList();
            //合計の降順: 完了 350 → 進行中 100。鍵は { value, text } (候補値のコードと表示名)
            Assert.That(groups.Select(g => g.GetProperty("key").GetProperty("Status").GetProperty("text").GetString()), Is.EqualTo(new[] { "完了", "進行中" }));
            Assert.That(groups.Select(g => g.GetProperty("key").GetProperty("Status").GetProperty("value").GetString()), Is.EqualTo(new[] { "20", "10" }));
            Assert.That(groups[0].GetProperty("sum_Amount").GetDecimal(), Is.EqualTo(350m));
            Assert.That(groups[0].GetProperty("count").GetInt32(), Is.EqualTo(2));
            Assert.That(groups[1].GetProperty("sum_Amount").GetDecimal(), Is.EqualTo(100m));
            Assert.That(progress.Texts, Does.Contain("状態別の金額"));

            //並びを指定しなければグループ化した項目の昇順
            var natural = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "count" } },
                ["groupBy"] = new[] { new { Field = "Status" } },
            });
            Assert.That(natural.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("key").GetProperty("Status").GetProperty("value").GetString()), Is.EqualTo(new[] { "10", "20" }));
        }

        [Test]
        public async Task aggregate_recordsは集計後の絞り込みと上限が効き上限で切れたことを伝える()
        {
            await _db.ExecuteAsync(Ds, "UPDATE AppUsers SET Rank = 30 WHERE Id = '2'", new());
            var tools = Create(o => o.MaxGroups = 5).CreateTools(Context("2")).ToList();

            //having: 合計 100 以上のタイトル (机 100・棚 300)。limit 1 で上位 1 件 → limited
            var top = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "sum", Field = "Amount" } },
                ["groupBy"] = new[] { new { Field = "Title" } },
                ["having"] = new object[] { new { MeasureIndex = 0, Comparison = "GreaterThanOrEqual", Value = 100 } },
                ["sort"] = new object[] { new { Target = "measure", Index = 0, Descending = true } },
                ["limit"] = 1,
            });
            Assert.That(ErrorOf(top), Is.Null);
            var groups = top.GetProperty("groups").EnumerateArray().ToList();
            Assert.That(groups.Select(g => g.GetProperty("key").GetProperty("Title").GetString()), Is.EqualTo(new[] { "棚" }));
            Assert.That(top.GetProperty("groupCount").GetInt32(), Is.EqualTo(2), "絞り込み後の総数");
            Assert.That(top.GetProperty("limited").GetBoolean(), Is.True);

            //limit は MaxGroups で頭打ち
            var many = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "count" } },
                ["groupBy"] = new[] { new { Field = "Title" } },
                ["limit"] = 1000,
            });
            Assert.That(many.GetProperty("groups").GetArrayLength(), Is.EqualTo(3));
            Assert.That(many.GetProperty("limited").GetBoolean(), Is.False);

            var badIndex = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "count" } },
                ["having"] = new object[] { new { MeasureIndex = 3, Comparison = "Equal", Value = 1 } },
            });
            Assert.That(badIndex.GetProperty("error").GetString(), Does.Contain("measureIndex"));
        }

        [Test]
        public async Task aggregate_recordsは日付を月で丸めリンク先の項目でも分けられる()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            var byMonth = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "count" } },
                ["groupBy"] = new[] { new { Field = "OrderedOn", DateUnit = "month" } },
            });
            //日付の鍵は { value: 期間の開始日, text: 単位の見出し }
            var months = byMonth.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetProperty("OrderedOn").GetProperty("text").GetString()!, g => g.GetProperty("count").GetInt32());
            Assert.That(months, Is.EqualTo(new Dictionary<string, int> { ["2026-01"] = 2, ["2026-02"] = 1 }));
            Assert.That(byMonth.GetProperty("groups")[0].GetProperty("key").GetProperty("OrderedOn").GetProperty("value").GetString(), Is.EqualTo("2026-01-01"));

            //四半期は年度の開始月で切る (4 月始まりなら 2026-01 は FY2025 Q4)。countDistinct も DB 側
            var byQuarter = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "countDistinct", Field = "Customer" } },
                ["groupBy"] = new[] { new { Field = "OrderedOn", DateUnit = "quarter", FiscalYearStartMonth = 4 } },
            });
            var quarter = byQuarter.GetProperty("groups").EnumerateArray().Single();
            Assert.That(quarter.GetProperty("key").GetProperty("OrderedOn").GetProperty("text").GetString(), Is.EqualTo("FY2025 Q4"));
            Assert.That(quarter.GetProperty("countDistinct_Customer").GetInt32(), Is.EqualTo(2));

            var badUnit = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "count" } },
                ["groupBy"] = new[] { new { Field = "Title", DateUnit = "month" } },
            });
            Assert.That(badUnit.GetProperty("error").GetString(), Does.Contain("日付・日時ではない"));

            var byCustomer = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "count" } },
                ["groupBy"] = new[] { new { Field = "Customer.Name" } },
            });
            var customers = byCustomer.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetProperty("Customer.Name").GetString()!, g => g.GetProperty("count").GetInt32());
            Assert.That(customers, Is.EqualTo(new Dictionary<string, int> { ["青空商事"] = 2, ["緑山工業"] = 1 }));
        }

        [Test]
        public async Task 読めない項目の集計は拒否され集計関数の誤りはエラーになる()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            //Rank 10 には Amount が見えない → 本体の集計 API が拒否する (accessDenied)
            var hidden = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "sum", Field = "Amount" } },
            });
            Assert.That(hidden.GetProperty("accessDenied").GetBoolean(), Is.True);

            var bad = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "median", Field = "Amount" } },
            });
            Assert.That(bad.GetProperty("error").GetString(), Does.Contain("median"));

            var wrongType = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "sum", Field = "Title" } },
            });
            Assert.That(wrongType.GetProperty("error").GetString(), Does.Contain("sum は使えません"));
        }

        [Test]
        public async Task cross_tabは行と列の表と合計を返す()
        {
            await _db.ExecuteAsync(Ds, "UPDATE AppUsers SET Rank = 30 WHERE Id = '2'", new());
            var tools = Create().CreateTools(Context("2")).ToList();

            //担当 A の 3 行: 進行中 机 100 (1 月) / 完了 椅子 50 (1 月) / 完了 棚 300 (2 月)
            var table = await InvokeAsync(tools, "cross_tab", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "状態 × 月",
                ["rows"] = new[] { new { Field = "Status" } },
                ["columns"] = new[] { new { Field = "OrderedOn", DateUnit = "month" } },
                ["measures"] = new object[] { new { Function = "sum", Field = "Amount" }, new { Function = "count" } },
            });

            Assert.That(ErrorOf(table), Is.Null);
            Assert.That(table.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("text").GetString()), Is.EqualTo(new[] { "進行中", "完了" }));
            Assert.That(table.GetProperty("rows")[0].GetProperty("key").GetProperty("Status").GetProperty("value").GetString(), Is.EqualTo("10"));
            Assert.That(table.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("text").GetString()), Is.EqualTo(new[] { "2026-01", "2026-02" }));
            Assert.That(table.GetProperty("measures").EnumerateArray().Select(m => m.GetString()), Is.EqualTo(new[] { "sum_Amount", "count" }));

            static decimal? Cell(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetDecimal();
            var sum = table.GetProperty("cells").GetProperty("sum_Amount").EnumerateArray().Select(r => r.EnumerateArray().Select(Cell).ToArray()).ToArray();
            Assert.That(sum, Is.EqualTo(new decimal?[][] { new decimal?[] { 100, null }, new decimal?[] { 50, 300 } }));
            Assert.That(table.GetProperty("rowTotals").GetProperty("sum_Amount").EnumerateArray().Select(Cell), Is.EqualTo(new decimal?[] { 100, 350 }));
            Assert.That(table.GetProperty("columnTotals").GetProperty("sum_Amount").EnumerateArray().Select(Cell), Is.EqualTo(new decimal?[] { 150, 300 }));
            Assert.That(table.GetProperty("grandTotals").GetProperty("sum_Amount").GetDecimal(), Is.EqualTo(450m));
            Assert.That(table.GetProperty("grandTotals").GetProperty("count").GetInt32(), Is.EqualTo(3));
            Assert.That(table.GetProperty("totalCount").GetInt32(), Is.EqualTo(3));

            //列なし = 行だけの表。セルは行ごとの値、合計は総計だけ
            var rowsOnly = await InvokeAsync(tools, "cross_tab", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["rows"] = new[] { new { Field = "Status" } },
                ["measures"] = new object[] { new { Function = "count" } },
                ["sort"] = new object[] { new { Target = "measure", Index = 0, Descending = true } },
            });
            Assert.That(rowsOnly.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("text").GetString()), Is.EqualTo(new[] { "完了", "進行中" }));
            Assert.That(rowsOnly.GetProperty("columns").GetArrayLength(), Is.EqualTo(0));
            Assert.That(rowsOnly.GetProperty("cells").GetProperty("count").EnumerateArray().Select(c => c.GetInt32()), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(rowsOnly.TryGetProperty("rowTotals", out _), Is.False);
            Assert.That(rowsOnly.GetProperty("grandTotals").GetProperty("count").GetInt32(), Is.EqualTo(3));
        }

        [Test]
        public async Task cross_tabも権限で拒否されセル数の上限を超えると表を作らない()
        {
            var denied = await InvokeAsync(Create().CreateTools(Context("1")).ToList(), "cross_tab", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["rows"] = new[] { new { Field = "Status" } },
                ["measures"] = new object[] { new { Function = "count" } },
            });
            Assert.That(denied.GetProperty("accessDenied").GetBoolean(), Is.True);

            var tooBig = await InvokeAsync(Create(o => o.MaxCrossTabCells = 2).CreateTools(Context("2")).ToList(), "cross_tab", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["rows"] = new[] { new { Field = "Status" } },
                ["columns"] = new[] { new { Field = "OrderedOn", DateUnit = "month" } },
                ["measures"] = new object[] { new { Function = "count" } },
            });
            Assert.That(tooBig.TryGetProperty("error", out _), Is.True);
        }

        [Test]
        public async Task get_recordは1件を子一覧ごと返し無いか読めなければエラーになる()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            var record = await InvokeAsync(tools, "get_record", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["id"] = "1" });
            Assert.That(ErrorOf(record), Is.Null);
            Assert.That(record.GetProperty("row").GetProperty("Title").GetString(), Is.EqualTo("机"));
            Assert.That(record.GetProperty("row").TryGetProperty("Amount", out _), Is.False, "Rank 10 には Amount が見えない");
            var lines = record.GetProperty("children").GetProperty("Lines");
            Assert.That(lines.GetProperty("module").GetString(), Is.EqualTo("OrderLine"));
            Assert.That(lines.GetProperty("rowCount").GetInt32(), Is.EqualTo(2), lines.ToString());
            Assert.That(lines.GetProperty("rows").EnumerateArray().Select(r => r.TryGetProperty("Item", out var item) ? item.GetString() : null), Is.EqualTo(new[] { "天板", "脚" }), lines.ToString());
            //件数上限つきの一覧は行を含めず、読み方だけ添える
            var paged = record.GetProperty("children").GetProperty("PagedLines");
            Assert.That(paged.GetProperty("rowCount").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(paged.GetProperty("note").GetString(), Does.Contain("find_records"));
            Assert.That(paged.TryGetProperty("rows", out _), Is.False);

            //結果が大きすぎれば子一覧の行を半分に減らして truncated
            var fullLength = record.GetRawText().Length;
            var small = await InvokeAsync(Create(o => o.MaxResultChars = fullLength - 1).CreateTools(Context("2")).ToList(), "get_record", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["id"] = "1" });
            var trimmed = small.GetProperty("children").GetProperty("Lines");
            Assert.That(trimmed.GetProperty("rows").GetArrayLength(), Is.EqualTo(1), small.ToString());
            Assert.That(trimmed.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(trimmed.GetProperty("rowCount").GetInt32(), Is.EqualTo(2), "総数はそのまま");

            //担当 B の行 (4) は行の条件で読めない = 無いのと同じ
            var hidden = await InvokeAsync(tools, "get_record", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["id"] = "4" });
            Assert.That(ErrorOf(hidden), Does.Contain("読めません"));

            var denied = await InvokeAsync(Create().CreateTools(Context("1")).ToList(), "get_record", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["id"] = "1" });
            Assert.That(denied.GetProperty("accessDenied").GetBoolean(), Is.True);
        }

        [Test]
        public async Task 読み取りの時間予算を使い切ると以後はDBへ行かずに断る()
        {
            var context = Context("2");
            var budget = QueryTimeBudget.Of(context.Items, 1);
            budget.Add(TimeSpan.FromSeconds(2));
            var tools = Create(o => o.MaxQuerySecondsPerReply = 1).CreateTools(context).ToList();
            foreach (var (name, args) in new (string, Dictionary<string, object?>)[]
            {
                ("find_records", new() { ["moduleName"] = "Order", ["purpose"] = "p" }),
                ("get_record", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["id"] = "1" }),
                ("aggregate_records", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["measures"] = new object[] { new { Function = "count" } } }),
                ("cross_tab", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["rows"] = new[] { new { Field = "Status" } }, ["measures"] = new object[] { new { Function = "count" } } }),
            })
            {
                Assert.That(ErrorOf(await InvokeAsync(tools, name, args)), Does.Contain("使い切りました"), name);
            }
        }

        [Test]
        public async Task 読み取りの実行時間は予算に足されタイムアウトは接続に入る()
        {
            var context = Context("2");
            var tools = Create(o => { o.MaxQuerySecondsPerReply = 45; o.CommandTimeoutSeconds = 3; }).CreateTools(context).ToList();
            await InvokeAsync(tools, "find_records", new() { ["moduleName"] = "Order", ["purpose"] = "p" });
            Assert.That(((QueryTimeBudget)context.Items[QueryTimeBudget.ItemKey]).Remaining, Is.LessThan(TimeSpan.FromSeconds(45)));
            Assert.That(_db.CommandTimeoutSeconds, Is.EqualTo(3), "スコープの IDbAccessor にタイムアウトが入る");
        }

        [Test]
        public async Task 同時実行の上限に達していて空かなければ混雑を返す()
        {
            const int max = 7;  //他のテストと鍵 (データソース名 + 上限) が重ならない値
            var gate = QueryGate.Get(Ds, max);
            for (var i = 0; i < max; i++) await gate.WaitAsync();
            try
            {
                var tools = Create(o => { o.MaxConcurrentQueries = max; o.CommandTimeoutSeconds = 1; }).CreateTools(Context("2")).ToList();
                var json = await InvokeAsync(tools, "find_records", new() { ["moduleName"] = "Order", ["purpose"] = "p" });
                Assert.That(ErrorOf(json), Does.Contain("混み合っています"));
            }
            finally
            {
                gate.Release(max);
            }
        }

        //Order に意味検索の索引 (SemanticSearchField) を足す。SQLite はベクトル検索できないので、ここで見るのは配線と権限 (距離の計算は SemanticSearchVectorDbTest の実 DB)
        void AddSemanticSearchField(Action<SemanticSearchFieldDesign>? configure = null)
        {
            var field = new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector_v" };
            configure?.Invoke(field);
            _designData.Modules.Find("Order")!.Fields.Add(field);
        }

        ModuleDataAccessToolSet CreateWithEmbedding(IEmbeddingProvider? provider)
            => new(OpenScope, () => _designData, new ModuleDataAccessOptions(), () => provider);

        [Test]
        public async Task 意味検索は索引のあるモジュールと埋め込みがあるときだけ付き本体の一覧検索として走る()
        {
            //索引なし・埋め込みなしでは付かない
            Assert.That(CreateWithEmbedding(new FakeEmbeddingProvider()).CreateTools(Context("2")).Select(t => t.Name), Does.Not.Contain("search_records"));
            AddSemanticSearchField();
            Assert.That(CreateWithEmbedding(null).CreateTools(Context("2")).Select(t => t.Name), Does.Not.Contain("search_records"));

            var toolSet = CreateWithEmbedding(new FakeEmbeddingProvider());
            Assert.That(toolSet.CreateTools(Context("2")).Select(t => t.Name), Does.Contain("search_records"));
            Assert.That(toolSet.GetInstructions(Context("2")), Does.Contain("search_records").And.Contain("Order"));

            //本体の一覧検索 → SemanticSearchField が SQL を作る。SQLite はベクトル検索できないのでその理由が返る (= 配線は通っている)
            var result = await InvokeAsync(toolSet.CreateTools(Context("2")).ToList(), "search_records", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["query"] = "机" });
            Assert.That(ErrorOf(result), Does.Contain("SQLite"));

            //意味検索できないモジュール
            var notSearchable = await InvokeAsync(toolSet.CreateTools(Context("2")).ToList(), "search_records", new() { ["moduleName"] = "Customer", ["purpose"] = "p", ["query"] = "机" });
            Assert.That(ErrorOf(notSearchable), Does.Contain("意味検索できません"));
        }

        [Test]
        public async Task 意味検索もモジュールと索引の項目の読み取り権限で拒否される()
        {
            //Order を読めない人 (Rank 1)
            AddSemanticSearchField();
            var denied = await InvokeAsync(CreateWithEmbedding(new FakeEmbeddingProvider()).CreateTools(Context("1")).ToList(), "search_records", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["query"] = "机" });
            Assert.That(denied.GetProperty("accessDenied").GetBoolean(), Is.True);

            //索引の項目 (Search) を読めない人 (Rank 10。Search は Rank 20 以上だけ)
            var permission = new PermissionFieldDesign { Name = "SearchPermission", TargetFields = { "Search" } };
            permission.ReadCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            _designData.Modules.Find("Order")!.Fields.Add(permission);
            var fieldDenied = await InvokeAsync(CreateWithEmbedding(new FakeEmbeddingProvider()).CreateTools(Context("2")).ToList(), "search_records", new() { ["moduleName"] = "Order", ["purpose"] = "p", ["query"] = "机" });
            Assert.That(fieldDenied.GetProperty("accessDenied").GetBoolean(), Is.True);
        }

        //実際の Azure OpenAI で ModuleDataAccessAgent を通す (課金あり・ネットワーク要)。cross_tab / aggregate_records をモデルが使い、権限の範囲 (担当 A の 3 行) で正しい数字を答えることを見る
        [Test, Explicit("実 Azure OpenAI を呼ぶ。AZURE_OPENAI_ENDPOINT / KEY / MODEL を設定して明示的に実行する")]
        public async Task 実AIで状態と月のクロス集計を頼むとcross_tabで答える()
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
                Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL が未設定");
            var client = new Azure.AI.OpenAI.AzureOpenAIClient(new Uri(endpoint!), new Azure.AzureKeyCredential(key!));
            await _db.ExecuteAsync(Ds, "UPDATE AppUsers SET Rank = 30 WHERE Id = '2'", new());

            var agent = new ModuleDataAccessAgent(() => client.GetChatClient(model).AsIChatClient(), OpenScope, () => _designData, null, new ModuleDataAccessOptions { StreamPartialReplies = false });
            var progress = new Progress();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var reply = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "real", Message = "受注 (Order) の金額を、状態を行・受注日の月を列にしたクロス集計表で見せてください。合計も付けて。", UserName = "2" },
                progress, cts.Token);
            TestContext.Out.WriteLine(string.Join(" | ", progress.Texts));
            TestContext.Out.WriteLine(reply.Content);

            //担当 A の 3 行: 進行中 1 月 100 / 完了 1 月 50・2 月 300 → 行計 100・350、総計 450
            Assert.That(reply.Content, Does.Contain("<table"));
            Assert.That(reply.Content, Does.Contain("350"));
            Assert.That(reply.Content, Does.Contain("450"));
            Assert.That(progress.Texts.Any(t => t.Contains("cross_tab")), Is.True, "cross_tab が使われた");

            var follow = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "real", Message = "得意先ごとの受注件数を多い順に教えて。一言で。", UserName = "2" },
                progress, cts.Token);
            TestContext.Out.WriteLine(follow.Content);
            Assert.That(follow.Content, Does.Contain("青空商事"));
        }

        [Test]
        public async Task Agentはツールを結線しシステムプロンプトに権限の説明が入る()
        {
            var client = new FakeChatClient()
                .Call("find_records", new Dictionary<string, object?> { ["moduleName"] = "Order", ["purpose"] = "一覧", ["fields"] = new[] { "Title" } })
                .Text("受注は 3 件です。");
            var agent = new ModuleDataAccessAgent(() => client, OpenScope, () => _designData, null, new ModuleDataAccessOptions { StreamPartialReplies = false });

            var reply = await agent.ReplyAsync(new AIChatAgentRequest { ConversationId = "c", Message = "受注は何件", UserName = "2" }, new Progress(), CancellationToken.None);

            Assert.That(reply.Content, Does.Contain("受注は 3 件です。"));
            Assert.That(client.Options[0]!.Tools!.Select(t => t.Name), Is.EquivalentTo(new[] { "list_modules", "describe_module", "find_records", "get_record", "aggregate_records", "cross_tab", "render_chart" }));
            Assert.That(client.Calls[0][0].Text, Does.Contain("画面で見られるレコードと項目だけ"));
            //ツールの結果 (担当 A の 3 行) が次の問い合わせに渡っている
            var toolResult = client.Calls[1].SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Single();
            Assert.That(toolResult.Result?.ToString(), Does.Contain("\"totalCount\":3"));
        }
    }
}
