using Azure;
using Azure.AI.OpenAI;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// 実際の Azure OpenAI と pgvector 入りの PostgreSQL で意味検索を通す (課金あり・ネットワーク要のため Explicit)。
    /// 環境変数 AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL / AZURE_OPENAI_EMBEDDING_MODEL (埋め込みのデプロイ名) /
    /// AZURE_OPENAI_EMBEDDING_DIMENSIONS (省略時 1536) / SEMANTIC_SEARCH_PG_CONNECTION (CREATE EXTENSION vector が済んだ PostgreSQL の接続文字列)。
    /// 実行: dotnet test --filter "FullyQualifiedName~SemanticSearchRealAITest"
    /// </summary>
    [Explicit("実 Azure OpenAI と PostgreSQL (pgvector) を使う。AZURE_OPENAI_* と SEMANTIC_SEARCH_PG_CONNECTION を設定して明示的に実行する")]
    public class SemanticSearchRealAITest : IAuthenticationContext
    {
        const string Ds = "AiDb";
        string _table = string.Empty;
        DataSource[] _dataSources = Array.Empty<DataSource>();
        DesignData _design = null!;
        Func<IEmbeddingGenerator<string, Embedding<float>>> _embedding = null!;
        Func<IChatClient> _chat = null!;

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

        [SetUp]
        public async Task SetUp()
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL");
            var embeddingModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL");
            var connection = Environment.GetEnvironmentVariable("SEMANTIC_SEARCH_PG_CONNECTION");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(embeddingModel) || string.IsNullOrEmpty(connection))
                Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL / AZURE_OPENAI_EMBEDDING_MODEL / SEMANTIC_SEARCH_PG_CONNECTION が未設定");
            var dimensions = int.TryParse(Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DIMENSIONS"), out var d) ? d : 1536;
            var client = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(key!));
            _chat = () => client.GetChatClient(model).AsIChatClient();
            _embedding = () => client.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

            DbAccessor.ClearTableDefinitionCache();
            _table = $"semantic_real_{Guid.NewGuid():N}"[..24];
            _dataSources = new[] { new DataSource { Name = Ds, DataSourceType = DataSourceType.PostgreSQL, ConnectionString = connection! } };
            await using var db = new DbAccessor(_dataSources);
            //本体はベクトルをテキストで書くので、pgvector の列は生成列でキャストする (docs/SemanticSearchField.md の構成そのまま)
            await db.ExecuteAsync(Ds, $"CREATE TABLE {_table} (id SERIAL PRIMARY KEY, subject TEXT, body TEXT, search_text TEXT, search_vector TEXT, " +
                $"search_vector_v vector({dimensions}) GENERATED ALWAYS AS (search_vector::vector) STORED)", new());
            var rows = new[]
            {
                ("納期の確認", "先週注文した商品がまだ届きません。いつ届くか教えてください。"),
                ("請求書の宛名変更", "請求書の宛名を部署名入りにして再発行してほしい。"),
                ("出荷遅延のお詫び対応", "出荷が遅れて納期に間に合わなかった。今後の再発防止策を求められている。"),
                ("ログインできない", "パスワードを忘れてしまい管理画面に入れない。"),
                ("見積の再送", "先日の見積書をもう一度メールで送ってほしい。"),
            };
            foreach (var (subject, body) in rows)
                await db.ExecuteAsync(Ds, $"INSERT INTO {_table} (subject, body) VALUES (@p1, @p2)", new() { ["@p1"] = subject, ["@p2"] = body });
            await db.CommitAsync();

            _design = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = _table };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector_v" });
            m.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(m);
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_dataSources.Length == 0 || string.IsNullOrEmpty(_table)) return;
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, $"DROP TABLE IF EXISTS {_table}", new());
            await db.CommitAsync();
        }

        [Test]
        public async Task 再索引して納期に関する似た問い合わせをAgentがDBのベクトル検索で探す()
        {
            var indexer = new SemanticSearchIndexer(_embedding);
            await using var db = new DbAccessor(_dataSources);
            var io = new IndexingModuleDataIO(_design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>()), indexer);
            Assert.That(await SemanticSearchIndexer.ReindexAsync(io, _design, "Inquiry"), Is.EqualTo(5));
            await db.CommitAsync();

            var indexed = await db.QueryAsync(Ds, $"select count(*) as c from {_table} where search_vector_v is not null", new());
            Assert.That(Convert.ToInt32(indexed.Single()["c"]), Is.EqualTo(5), "テキスト列から生成列にベクトルが入る");

            var agent = new RawDataAccessAgent(_chat, () => new DbAccessor(_dataSources), () => _design, null,
                new RawDataAccessOptions { DataSourceNames = { Ds } }, embeddingGeneratorFactory: _embedding);
            var progress = new Progress();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var reply = await agent.ReplyAsync(new AIChatAgentRequest { ConversationId = "c1", Message = "商品の到着が遅れているという問い合わせに似た過去の事例を 2 件挙げて", UserName = "tester" }, progress, cts.Token);

            TestContext.Out.WriteLine(reply.Content);
            TestContext.Out.WriteLine(string.Join("\n", progress.Texts));
            Assert.That(progress.Texts.Any(t => t.Contains("似た記録")), Is.True, "search_records が呼ばれた");
            Assert.That(reply.Content, Does.Contain("納期").Or.Contain("遅"));
            Assert.That(reply.Content, Does.Not.Contain("パスワード"), "無関係な事例は挙げない");
        }
    }
}
