using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// RawDataAccess の DB 負荷の上限を実 DB で確かめる (手動)。AI 用の読み取り専用ユーザーで接続し、
    /// 統計 (行数・索引) が読めること、行数上限での中止が DB の仕事を止めること、タイムアウトが効くことを見る。
    /// 表 ai_load_big (id PK, created DATE, customer_id INT, amount INT, note NVARCHAR(200)・200 万行・created と customer_id に索引) を先に作っておく。
    /// 接続文字列は環境変数 RAWDATA_LOAD_MSSQL_CONNECTION / RAWDATA_LOAD_PG_CONNECTION。
    /// </summary>
    [Explicit]
    public class RawDataAccessLoadRealDbTest
    {
        const string Ds = "AiLoad";

        sealed class Progress : IAIChatProgress
        {
            public void Report(string progressText) { }
            public void ReportPartial(AIChatReply partialReply) { }
        }

        static DataSource[] Sources(DataSourceType type, string variable)
        {
            var connection = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(connection)) Assert.Ignore($"{variable} が未設定");
            return new[] { new DataSource { Name = Ds, DataSourceType = type, ConnectionString = connection } };
        }

        static AIChatToolContext Context()
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u" }, new Progress(), CancellationToken.None, null);

        static async Task<(string Json, TimeSpan Elapsed)> ExecuteAsync(DataSource[] sources, Action<RawDataAccessOptions> configure, string sql)
        {
            var options = new RawDataAccessOptions { DataSourceNames = { Ds } };
            configure(options);
            var tools = new RawDataAccessToolSet(() => new DbAccessor(sources), null, options).CreateTools(Context()).ToList();
            var function = tools.OfType<AIFunction>().Single(t => t.Name == "execute_sql");
            var stopwatch = Stopwatch.StartNew();
            var result = await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["sql"] = sql, ["purpose"] = "" }));
            var json = result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
            return (json, stopwatch.Elapsed);
        }

        static IEnumerable<TestCaseData> Cases()
        {
            yield return new TestCaseData(DataSourceType.SQLServer, "RAWDATA_LOAD_MSSQL_CONNECTION", "dbo.ai_load_big",
                "SELECT COUNT_BIG(*) FROM ai_load_big a CROSS JOIN ai_load_big b WHERE a.amount + b.amount = 7").SetArgDisplayNames("SQLServer");
            yield return new TestCaseData(DataSourceType.PostgreSQL, "RAWDATA_LOAD_PG_CONNECTION", "ai_load_big",
                "SELECT COUNT(*) FROM ai_load_big a CROSS JOIN ai_load_big b WHERE a.amount + b.amount = 7").SetArgDisplayNames("PostgreSQL");
        }

        [TestCaseSource(nameof(Cases))]
        public async Task 読み取り専用ユーザーで統計が読める(DataSourceType type, string variable, string table, string heavySql)
        {
            var sources = Sources(type, variable);
            var errors = new List<string>();
            var stats = await DbSchemaReader.ReadTableStatsAsync(() => new DbAccessor(sources), Ds, 15, e => errors.Add(e.Message), CancellationToken.None);
            TestContext.Out.WriteLine($"errors: {string.Join(" / ", errors)}");
            foreach (var e in stats) TestContext.Out.WriteLine($"{e.Key}: rows={e.Value.Rows} index={string.Join(",", e.Value.IndexColumns)}");
            Assert.That(errors, Is.Empty);
            Assert.That(stats.TryGetValue(table, out var big), Is.True, "表名の形は get_schema と同じ");
            Assert.That(big!.Rows, Is.InRange(1_500_000L, 2_500_000L));
            Assert.That(big.IndexColumns, Does.Contain("created").And.Contain("customer_id"));
        }

        [TestCaseSource(nameof(Cases))]
        public async Task 行数上限での中止は残りの行をDBに送らせない(DataSourceType type, string variable, string table, string heavySql)
        {
            var sources = Sources(type, variable);
            const string sql = "SELECT * FROM ai_load_big";
            await ExecuteAsync(sources, o => { }, "SELECT 1 AS x"); //接続を温める
            var canceled = await ExecuteAsync(sources, o => { o.CancelQueryAtRowLimit = true; o.MaxResultChars = 1_000_000; }, sql);
            var drained = await ExecuteAsync(sources, o => { o.CancelQueryAtRowLimit = false; o.MaxResultChars = 1_000_000; o.CommandTimeoutSeconds = 0; o.MaxQuerySecondsPerReply = 0; }, sql);
            TestContext.Out.WriteLine($"cancel: {canceled.Elapsed.TotalMilliseconds:N0} ms / no cancel: {drained.Elapsed.TotalMilliseconds:N0} ms");
            foreach (var json in new[] { canceled.Json, drained.Json })
            {
                using var doc = JsonDocument.Parse(json);
                Assert.That(doc.RootElement.GetProperty("rowCount").GetInt32(), Is.EqualTo(200), json.Length > 300 ? json[..300] : json);
                Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
            }
            Assert.That(canceled.Elapsed, Is.LessThan(drained.Elapsed));
        }

        [TestCaseSource(nameof(Cases))]
        public async Task 重い集計はタイムアウトで止まり時間切れを伝える(DataSourceType type, string variable, string table, string heavySql)
        {
            var sources = Sources(type, variable);
            var (json, elapsed) = await ExecuteAsync(sources, o => o.CommandTimeoutSeconds = 2, heavySql);
            TestContext.Out.WriteLine($"{elapsed.TotalMilliseconds:N0} ms: {json}");
            var error = JsonDocument.Parse(json).RootElement.GetProperty("error").GetString();
            Assert.That(error, Does.Contain("時間切れ"));
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }
    }
}
