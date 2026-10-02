using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess;
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
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "Amount" } };
            permission.ReadCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            order.Fields.Add(permission);
            d.AddModule(order);
            return d;
        }

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す (ホストの DataService(userId) に相当)
        Task<ModuleDataAccessScope> OpenScope(string userId)
        {
            _currentUserId = userId;
            var io = new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            return Task.FromResult(new ModuleDataAccessScope(io));
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
            });

            Assert.That(result.GetProperty("scannedRows").GetInt32(), Is.EqualTo(3), "担当 B の行は数えない");
            Assert.That(result.GetProperty("truncated").GetBoolean(), Is.False);
            var groups = result.GetProperty("groups").EnumerateArray().ToList();
            //最初の集計値 (sum) の降順: 完了 350 → 進行中 100。鍵は表示名
            Assert.That(groups.Select(g => g.GetProperty("key").GetProperty("Status").GetString()), Is.EqualTo(new[] { "完了", "進行中" }));
            Assert.That(groups[0].GetProperty("sum_Amount").GetDecimal(), Is.EqualTo(350m));
            Assert.That(groups[0].GetProperty("count").GetInt32(), Is.EqualTo(2));
            Assert.That(groups[1].GetProperty("sum_Amount").GetDecimal(), Is.EqualTo(100m));
            Assert.That(progress.Texts, Does.Contain("状態別の金額"));
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
            var months = byMonth.GetProperty("groups").EnumerateArray().ToDictionary(g => g.GetProperty("key").GetProperty("OrderedOn").GetString()!, g => g.GetProperty("count").GetInt32());
            Assert.That(months, Is.EqualTo(new Dictionary<string, int> { ["2026-01"] = 2, ["2026-02"] = 1 }));

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
        public async Task 読めない項目の合計は数えられず集計関数の誤りはエラーになる()
        {
            var tools = Create().CreateTools(Context("2")).ToList();

            //Rank 10 には Amount が見えない → 合計は null (作らない)
            var hidden = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "sum", Field = "Amount" } },
            });
            Assert.That(hidden.GetProperty("groups").EnumerateArray().Single().GetProperty("sum_Amount").ValueKind, Is.EqualTo(JsonValueKind.Null));

            var bad = await InvokeAsync(tools, "aggregate_records", new()
            {
                ["moduleName"] = "Order",
                ["purpose"] = "p",
                ["measures"] = new object[] { new { Function = "median", Field = "Amount" } },
            });
            Assert.That(bad.GetProperty("error").GetString(), Does.Contain("median"));
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
            Assert.That(client.Options[0]!.Tools!.Select(t => t.Name), Is.EquivalentTo(new[] { "list_modules", "describe_module", "find_records", "aggregate_records", "render_chart" }));
            Assert.That(client.Calls[0][0].Text, Does.Contain("画面で見られるレコードと項目だけ"));
            //ツールの結果 (担当 A の 3 行) が次の問い合わせに渡っている
            var toolResult = client.Calls[1].SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Single();
            Assert.That(toolResult.Result?.ToString(), Does.Contain("\"totalCount\":3"));
        }
    }
}
