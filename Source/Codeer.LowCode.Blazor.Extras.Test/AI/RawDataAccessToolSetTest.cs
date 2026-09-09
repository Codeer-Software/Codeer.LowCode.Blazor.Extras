using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools;
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

        RawDataAccessToolSet Create(Action<RawDataAccessOptions>? configure = null, IModuleDesigns? modules = null)
        {
            var options = new RawDataAccessOptions { DataSourceName = Ds, MaxRows = 10 };
            configure?.Invoke(options);
            return new RawDataAccessToolSet(() => new DbAccessor(_dataSources), modules, options);
        }

        static AIChatToolContext Context(Progress? progress = null)
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u" }, progress ?? new Progress(), CancellationToken.None, null);

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

            var tools = Create(modules: designs.Modules).CreateTools(Context()).ToList();
            var schema = await InvokeAsync(tools, "get_schema");

            Assert.That(schema, Does.Contain("SQLite"));
            Assert.That(schema, Does.Contain("Orders [module 受注]"));
            Assert.That(schema, Does.Contain("Amount REAL  [金額]"));
            Assert.That(schema, Does.Contain("[状態; values: 未処理,0, 処理済,1]"));
            Assert.That(schema, Does.Contain("Secrets"));
        }

        [Test]
        public async Task 除外した表はスキーマに出ない()
        {
            var tools = Create(o => o.ExcludedTables.Add("secrets")).CreateTools(Context()).ToList();
            var schema = await InvokeAsync(tools, "get_schema");
            Assert.That(schema, Does.Contain("Orders"));
            Assert.That(schema, Does.Not.Contain("Secrets"));
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
    }
}
