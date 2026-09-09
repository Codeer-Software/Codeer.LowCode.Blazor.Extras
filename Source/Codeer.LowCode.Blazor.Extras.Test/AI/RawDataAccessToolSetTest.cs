using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>RawDataAccessToolSet: SQLite の実 DB でスキーマ取得と SELECT 実行、行数上限、SELECT 以外の拒否。</summary>
    public class RawDataAccessToolSetTest
    {
        const string Ds = "AiDb";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = Array.Empty<DataSource>();

        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) { }
        }

        [SetUp]
        public async Task SetUp()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), $"aichat_raw_{Guid.NewGuid():N}.db");
            _dataSources = new[] { new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" } };
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Customer TEXT, Amount REAL, Status INTEGER)", new());
            for (var i = 1; i <= 30; i++)
                await db.ExecuteAsync(Ds, $"INSERT INTO Orders (Id, Customer, Amount, Status) VALUES ({i}, 'C{i % 3}', {i * 10}, {i % 2})", new());
            await db.ExecuteAsync(Ds, "CREATE TABLE Secrets (Id INTEGER PRIMARY KEY, Token TEXT)", new());
        }

        [TearDown]
        public void TearDown()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        RawDataAccessToolSet Create(Action<RawDataAccessOptions>? configure = null, DesignData? design = null)
        {
            var options = new RawDataAccessOptions { DataSourceNames = { Ds }, MaxRows = 10 };
            configure?.Invoke(options);
            return new RawDataAccessToolSet(() => new DbAccessor(_dataSources), design == null ? null : () => design, options);
        }

        static AIChatToolContext Context(Progress? progress = null)
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u" }, progress ?? new Progress(), CancellationToken.None, null);

        //ツールの結果 JSON (非 ASCII はエスケープされる) から error を取り出す
        static string ErrorOf(string json) => JsonDocument.Parse(json).RootElement.GetProperty("error").GetString() ?? string.Empty;

        static async Task<string> InvokeAsync(IEnumerable<AITool> tools, string name, Dictionary<string, object?>? args = null)
        {
            var function = tools.OfType<AIFunction>().Single(t => t.Name == name);
            var result = await function.InvokeAsync(new AIFunctionArguments(args ?? new Dictionary<string, object?>()));
            return result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
        }

        [Test]
        public async Task get_schemaは表と列と方言を返しモジュール定義があれば業務名を添える()
        {
            var designs = new DesignData();
            var module = new ModuleDesign { Name = "受注", DataSourceName = Ds, DbTable = "Orders" };
            module.Fields.Add(new NumberFieldDesign { Name = "金額", DbColumn = "Amount" });
            var status = new SelectFieldDesign { Name = "状態", DbColumn = "Status" };
            status.Candidates.AddRange(new[] { "未処理,0", "処理済,1" });
            module.Fields.Add(status);
            designs.AddModule(module);

            var toolSet = Create(design: designs);
            var tools = toolSet.CreateTools(Context()).ToList();

            //引数なし = 目次 (表名・列数・モジュール名) だけ。列は出ない
            var index = await InvokeAsync(tools, "get_schema");
            Assert.That(index, Does.Contain("## データソース AiDb (SQLite"));
            Assert.That(index, Does.Contain("- Orders (4 列) [モジュール 受注]"));
            Assert.That(index, Does.Contain("- Secrets (2 列)"));
            Assert.That(index, Does.Not.Contain("Amount"));

            //tables 指定 = その表の列だけ、1 表 1 行
            var detail = await InvokeAsync(tools, "get_schema", new() { ["tables"] = new[] { "orders" } });
            Assert.That(detail, Does.Contain("- Orders [モジュール 受注](Id INTEGER, Customer TEXT, Amount REAL [金額], Status INTEGER [状態; 候補値: 0=未処理, 1=処理済])"));
            Assert.That(detail, Does.Not.Contain("Secrets"));

            var missing = await InvokeAsync(tools, "get_schema", new() { ["tables"] = new[] { "Nope" } });
            Assert.That(missing, Does.Contain("見つかりません"));

            //方言はプロンプト側にも出る (get_schema を呼ばなくても分かる)
            Assert.That(toolSet.GetInstructions(Context()), Does.Contain("AiDb = SQLite"));
        }

        [Test]
        public async Task 除外した表はスキーマに出ない()
        {
            var tools = Create(o => o.ExcludedTables.Add("secrets")).CreateTools(Context()).ToList();
            var schema = await InvokeAsync(tools, "get_schema");
            Assert.That(schema, Does.Contain("Orders"));
            Assert.That(schema, Does.Not.Contain("Secrets"));
            var detail = await InvokeAsync(tools, "get_schema", new() { ["tables"] = new[] { "Secrets" } });
            Assert.That(detail, Does.Contain("見つかりません"), "除外した表は名指ししても出ない");
        }

        [Test]
        public async Task execute_sqlはSELECTを実行し行数上限で切って続きありを伝える()
        {
            var progress = new Progress();
            var tools = Create().CreateTools(Context(progress)).ToList();

            var json = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT Customer, SUM(Amount) AS Total FROM Orders GROUP BY Customer ORDER BY Customer", ["purpose"] = "得意先別合計" });
            using var doc = JsonDocument.Parse(json);
            Assert.That(doc.RootElement.GetProperty("columns").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "Customer", "Total" }));
            Assert.That(doc.RootElement.GetProperty("rowCount").GetInt32(), Is.EqualTo(3));
            Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(progress.Texts, Does.Contain("得意先別合計"));

            var all = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT * FROM Orders ORDER BY Id;", ["purpose"] = "" });
            using var doc2 = JsonDocument.Parse(all);
            Assert.That(doc2.RootElement.GetProperty("rowCount").GetInt32(), Is.EqualTo(10), "MaxRows で切る");
            Assert.That(doc2.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        }

        [Test]
        public async Task SELECT以外と複文は実行せずエラーを返す()
        {
            var tools = Create().CreateTools(Context()).ToList();
            foreach (var sql in new[]
            {
                "DELETE FROM Orders",
                "UPDATE Orders SET Amount = 0",
                "SELECT 1; DROP TABLE Orders",
                "INSERT INTO Orders (Id) VALUES (99)",
                "PRAGMA table_info(Orders)",
                "WITH x AS (DELETE FROM Orders RETURNING Id) SELECT * FROM x",
            })
            {
                var json = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = sql, ["purpose"] = "" });
                Assert.That(json, Does.Contain("\"error\""), sql);
            }
            //拒否されたので行は残っている
            var count = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT COUNT(*) AS N FROM Orders", ["purpose"] = "" });
            Assert.That(count, Does.Contain("[30]"));
        }

        [Test]
        public void 文字列リテラルやコメントの中の語は拒否理由にしない()
        {
            Assert.That(RawDataAccessToolSet.Validate("SELECT * FROM Orders WHERE Customer = 'delete; drop'"), Is.Null);
            Assert.That(RawDataAccessToolSet.Validate("-- update later\nSELECT 1"), Is.Null);
            Assert.That(RawDataAccessToolSet.Validate("WITH t AS (SELECT 1 AS a) SELECT a FROM t"), Is.Null);
            Assert.That(RawDataAccessToolSet.Validate("SELECT * INTO NewTable FROM Orders"), Is.Not.Null);
            Assert.That(RawDataAccessToolSet.Validate(""), Is.Not.Null);
        }

        [Test]
        public async Task SQLの誤りは例外にせずエラーとして返す()
        {
            var tools = Create().CreateTools(Context()).ToList();
            var json = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT nope FROM Orders", ["purpose"] = "" });
            Assert.That(json, Does.Contain("\"error\""));
            Assert.That(json, Does.Contain("nope"));
        }
        [Test]
        public async Task 複数のデータソースはスキーマを分けて出しexecute_sqlはdataSourceで選ぶ()
        {
            var second = Path.Combine(Path.GetTempPath(), $"aichat_raw2_{Guid.NewGuid():N}.db");
            var sources = new[]
            {
                _dataSources[0],
                new DataSource { Name = "Other", DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={second}" },
            };
            try
            {
                await using (var db = new DbAccessor(sources))
                    await db.ExecuteAsync("Other", "CREATE TABLE Products (Id INTEGER PRIMARY KEY, Name TEXT)", new());

                var options = new RawDataAccessOptions { DataSourceNames = { Ds, "Other" } };
                var tools = new RawDataAccessToolSet(() => new DbAccessor(sources), null, options).CreateTools(Context()).ToList();

                var schema = await InvokeAsync(tools, "get_schema");
                Assert.That(schema, Does.Contain("## データソース AiDb"));
                Assert.That(schema, Does.Contain("## データソース Other"));
                Assert.That(schema.IndexOf("Products", StringComparison.Ordinal), Is.GreaterThan(schema.IndexOf("## データソース Other", StringComparison.Ordinal)));
                var only = await InvokeAsync(tools, "get_schema", new() { ["dataSource"] = "Other" });
                Assert.That(only, Does.Not.Contain("## データソース AiDb"));
                Assert.That(only, Does.Contain("Products"));

                var ok = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT COUNT(*) AS N FROM Products", ["purpose"] = "", ["dataSource"] = "other" });
                Assert.That(ok, Does.Contain("\"dataSource\":\"Other\""));
                Assert.That(ok, Does.Contain("[0]"));

                var missing = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT 1", ["purpose"] = "" });
                Assert.That(ErrorOf(missing), Does.Contain("dataSource を指定"));
                var unknown = await InvokeAsync(tools, "execute_sql", new() { ["sql"] = "SELECT 1", ["purpose"] = "", ["dataSource"] = "Nope" });
                Assert.That(ErrorOf(unknown), Does.Contain("使えるのは AiDb, Other"));
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(second)) File.Delete(second);
            }
        }

        [Test]
        public void データソースが空なら作れない()
        {
            Assert.Throws<ArgumentException>(() => new RawDataAccessToolSet(() => new DbAccessor(_dataSources), null, new RawDataAccessOptions()));
        }
    }
}
