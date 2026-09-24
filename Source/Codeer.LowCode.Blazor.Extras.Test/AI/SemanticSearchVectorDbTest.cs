using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// DB 側のベクトル検索を実 DB で通す (AI は使わない = FakeEmbeddingProvider)。
    /// PostgreSQL は pgvector の生成列 (テキスト列を ::vector でキャスト)、SQL Server 2025 は VECTOR 型列にテキストから暗黙変換で書く構成
    /// (docs/SemanticSearchField.md の「必要な DB 構成」そのまま) で、保存 → 再索引 → search_records → execute_sql の {embed:…} を確認する。
    /// 環境変数 SEMANTIC_SEARCH_PG_CONNECTION (CREATE EXTENSION vector 済みの PostgreSQL) / SEMANTIC_SEARCH_MSSQL_CONNECTION (SQL Server 2025) が
    /// 設定されているものだけ実行する (無ければ Ignore)。
    /// </summary>
    [Explicit("実 DB (pgvector PostgreSQL / SQL Server 2025) を使う。SEMANTIC_SEARCH_PG_CONNECTION / SEMANTIC_SEARCH_MSSQL_CONNECTION を設定して明示的に実行する")]
    public class SemanticSearchVectorDbTest : IAuthenticationContext
    {
        const string Ds = "VecDb";

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) { }
        }

        sealed class IndexingModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchIndexer indexer)
            : ModuleDataIO(design, auth, db, files)
        {
            protected override async Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
            {
                await indexer.ApplyAsync(design, data, isNewData: true);
                return await base.AddAsync(transactionId, moduleSubmitId, data);
            }

            protected override async Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
            {
                await indexer.ApplyAsync(design, data, isNewData: false);
                await base.UpdateAsync(transactionId, moduleSubmitId, data);
            }
        }

        static readonly (string Subject, string Body)[] Rows =
        {
            ("納期遅れの相談", "注文した商品がまだ届かない。納期を確認したい"),
            ("請求書の再発行", "宛名を変更して請求書を再発行してほしい"),
            ("納品が遅れている", "出荷が遅れて納期に間に合わない"),
            ("パスワードを忘れた", "ログインできない"),
        };

        static DesignData CreateDesign(string table, string vectorSearchColumn)
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = table };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body" });
            m.Fields.Add(new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "is_deleted" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = vectorSearchColumn });
            m.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(m);
            var frame = new PageFrameDesign { Name = "Main", IsApplicationRoot = true };
            frame.Left.Links.Add(new PageLink { Title = "問い合わせ", Module = "Inquiry", ModuleUrlSegment = "inquiries", ModulePageType = ModulePageType.List });
            ((IEditablePageFrameDesign)d.PageFrames).Add(frame);
            return d;
        }

        static async Task<JsonDocument> InvokeAsync(IEnumerable<AITool> tools, Dictionary<string, object?> args)
        {
            var function = tools.OfType<AIFunction>().Single(t => t.Name == "search_records");
            var result = await function.InvokeAsync(new AIFunctionArguments(args));
            var json = result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
            return JsonDocument.Parse(json);
        }

        static AIChatToolContext Context(Progress progress)
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u" }, progress, CancellationToken.None, null);

        /// <summary>保存 (索引付け) → 再索引 → DB 側検索 → {embed:…} 展開 SQL の実行、を 1 本で通す。</summary>
        async Task RunAsync(DataSource[] dataSources, string table, string vectorSearchColumn, string createTable, string dropTable, string embedSql)
        {
            DbAccessor.ClearTableDefinitionCache();
            var design = CreateDesign(table, vectorSearchColumn);
            var embedding = new FakeEmbeddingProvider();
            var indexer = new SemanticSearchIndexer(() => embedding);
            await using (var db = new DbAccessor(dataSources))
            {
                await db.ExecuteAsync(Ds, dropTable, new());
                await db.ExecuteAsync(Ds, createTable, new());
                await db.CommitAsync();
            }
            try
            {
                //保存経路: 新規行は文章が無くてもサーバーが組み立てて埋め込みを付ける (一括取込と同じ)
                await using (var db = new DbAccessor(dataSources))
                {
                    var io = new IndexingModuleDataIO(design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>()), indexer);
                    var submits = Rows.Select(r =>
                    {
                        var add = new ModuleData { Name = "Inquiry" };
                        add.Fields["Subject"] = new TextFieldData { Value = r.Subject };
                        add.Fields["Body"] = new TextFieldData { Value = r.Body };
                        return new ModuleSubmitData { ModuleName = "Inquiry", Add = { add } };
                    }).ToList();
                    var results = await io.SubmitWithTransactionAsync(submits);
                    Assert.That(results.Where(r => !string.IsNullOrEmpty(r.ExceptionMessage)).Select(r => r.ExceptionMessage), Is.Empty);
                    await db.CommitAsync();

                    //テキスト列から DB のベクトル列に入っている
                    var indexed = await db.QueryAsync(Ds, $"select count(*) as c from {table} where {vectorSearchColumn} is not null", new());
                    Assert.That(Convert.ToInt32(indexed.Single().Values.First()), Is.EqualTo(Rows.Length), "ベクトル列が埋まる");

                    //再索引も通る (Update 経路)
                    Assert.That(await indexer.ReindexAsync(io, db, design, "Inquiry", pageSize: 2), Is.EqualTo(Rows.Length));
                    await db.CommitAsync();
                }

                //DB 側検索 (search_records): 納期の 2 件が上位。論理削除した行は出ない
                var toolSet = new SemanticSearchToolSet(() => design, () => new DbAccessor(dataSources), () => embedding, [Ds]);
                var progress = new Progress();
                var tools = toolSet.CreateTools(Context(progress)).ToList();
                Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "search_records" }));
                Assert.That(toolSet.GetInstructions(Context(progress)), Does.Contain("Inquiry").And.Contain(vectorSearchColumn));

                using (var doc = await InvokeAsync(tools, new() { ["moduleName"] = "Inquiry", ["query"] = "納期が遅れている注文", ["top"] = 2 }))
                {
                    var root = doc.RootElement;
                    Assert.That(root.TryGetProperty("error", out var error) ? error.GetString() : null, Is.Null);
                    var results = root.GetProperty("results").EnumerateArray().ToList();
                    Assert.That(results.Count, Is.EqualTo(2));
                    var ids = results.Select(r => r.GetProperty("id").GetString()).ToList();
                    var texts = results.Select(r => r.GetProperty("text").GetString()!).ToList();
                    Assert.That(texts.All(t => t.Contains("納")), Is.True, string.Join(" | ", texts));
                    Assert.That(results[0].GetProperty("score").GetDouble(), Is.GreaterThanOrEqualTo(results[1].GetProperty("score").GetDouble()));
                    Assert.That(results[0].GetProperty("score").GetDouble(), Is.InRange(0, 1.0001), "semantic_score = 1 - コサイン距離");
                    Assert.That(results[0].GetProperty("url").GetString(), Is.EqualTo("/Main/inquiries/" + ids[0]));
                    Assert.That(progress.Texts, Is.Not.Empty);

                    //上位 1 件を論理削除すると検索から消える
                    await using var db = new DbAccessor(dataSources);
                    await db.ExecuteAsync(Ds, $"update {table} set is_deleted = @p1 where id = @p2", new() { ["@p1"] = true, ["@p2"] = Convert.ToInt32(ids[0]) });
                    await db.CommitAsync();
                }
                using (var doc = await InvokeAsync(tools, new() { ["moduleName"] = "Inquiry", ["query"] = "納期が遅れている注文", ["top"] = 4 }))
                {
                    var results = doc.RootElement.GetProperty("results").EnumerateArray().ToList();
                    Assert.That(results.Count, Is.EqualTo(Rows.Length - 1), "論理削除の行は出ない");
                }

                //execute_sql の {embed:…}: 置換した SQL がそのまま DB で実行できる
                var expanded = await toolSet.ExpandEmbeddingsAsync(Ds, embedSql.Replace("{table}", table).Replace("{col}", vectorSearchColumn), CancellationToken.None);
                Assert.That(expanded, Does.Not.Contain("{embed"));
                await using (var db = new DbAccessor(dataSources))
                {
                    var rows = (await db.QueryAsync(Ds, expanded, new())).ToList();
                    Assert.That(rows.Count, Is.EqualTo(2));
                    //上位 1 件は論理削除済みなので、残る「納」の行が先頭に来る (2 件目は別の話題)
                    Assert.That(Convert.ToString(rows[0]["subject"]), Does.Contain("納"), string.Join(" | ", rows.Select(r => Convert.ToString(r["subject"]))));
                }
            }
            finally
            {
                await using var db = new DbAccessor(dataSources);
                await db.ExecuteAsync(Ds, dropTable, new());
                await db.CommitAsync();
            }
        }

        [Test]
        public async Task PostgreSQL_pgvector()
        {
            var connection = Environment.GetEnvironmentVariable("SEMANTIC_SEARCH_PG_CONNECTION");
            if (string.IsNullOrEmpty(connection)) Assert.Ignore("SEMANTIC_SEARCH_PG_CONNECTION が未設定");
            var table = "semantic_vec_test";
            await RunAsync(
                [new DataSource { Name = Ds, DataSourceType = DataSourceType.PostgreSQL, ConnectionString = connection! }],
                table, "search_vector_v",
                $"CREATE TABLE {table} (id SERIAL PRIMARY KEY, subject TEXT, body TEXT, is_deleted BOOLEAN DEFAULT FALSE, search_text TEXT, search_vector TEXT, " +
                $"search_vector_v vector({FakeEmbeddingProvider.Dimensions}) GENERATED ALWAYS AS (search_vector::vector) STORED)",
                $"DROP TABLE IF EXISTS {table}",
                "select id, subject, 1 - ({col} <=> {embed:納期が遅れている注文}) as score from {table} where is_deleted = false order by {col} <=> {embed:納期が遅れている注文} limit 2");
        }

        [Test]
        public async Task SQLServer2025_VECTOR()
        {
            var connection = Environment.GetEnvironmentVariable("SEMANTIC_SEARCH_MSSQL_CONNECTION");
            if (string.IsNullOrEmpty(connection)) Assert.Ignore("SEMANTIC_SEARCH_MSSQL_CONNECTION が未設定");
            var table = "semantic_vec_test";
            await RunAsync(
                [new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLServer, ConnectionString = connection! }],
                table, "search_vector",
                $"CREATE TABLE {table} (id INT IDENTITY PRIMARY KEY, subject NVARCHAR(200), body NVARCHAR(MAX), is_deleted BIT DEFAULT 0, search_text NVARCHAR(MAX), " +
                $"search_vector VECTOR({FakeEmbeddingProvider.Dimensions}))",
                $"DROP TABLE IF EXISTS {table}",
                "select top (2) id, subject, 1 - VECTOR_DISTANCE('cosine', {col}, {embed:納期が遅れている注文}) as score from {table} where is_deleted = 0 order by VECTOR_DISTANCE('cosine', {col}, {embed:納期が遅れている注文})");
        }
    }
}
