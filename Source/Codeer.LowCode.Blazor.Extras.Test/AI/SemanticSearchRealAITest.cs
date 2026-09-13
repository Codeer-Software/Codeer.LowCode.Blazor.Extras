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
    /// 実際の Azure OpenAI で意味検索を通す (課金あり・ネットワーク要のため Explicit)。
    /// 環境変数 AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL に加えて AZURE_OPENAI_EMBEDDING_MODEL (埋め込みのデプロイ名)。
    /// 実行: dotnet test --filter "FullyQualifiedName~SemanticSearchRealAITest"
    /// </summary>
    [Explicit("実 Azure OpenAI を呼ぶ。AZURE_OPENAI_ENDPOINT / KEY / MODEL / EMBEDDING_MODEL を設定して明示的に実行する")]
    public class SemanticSearchRealAITest : IAuthenticationContext
    {
        const string Ds = "AiDb";
        string _dbFile = string.Empty;
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
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(embeddingModel))
                Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL / AZURE_OPENAI_EMBEDDING_MODEL が未設定");
            var client = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(key!));
            _chat = () => client.GetChatClient(model).AsIChatClient();
            _embedding = () => client.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();

            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"semantic_real_{Guid.NewGuid():N}.db");
            _dataSources = new[] { new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" } };
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, body TEXT, search_text TEXT, search_vector TEXT)", new());
            var rows = new[]
            {
                ("納期の確認", "先週注文した商品がまだ届きません。いつ届くか教えてください。"),
                ("請求書の宛名変更", "請求書の宛名を部署名入りにして再発行してほしい。"),
                ("出荷遅延のお詫び対応", "出荷が遅れて納期に間に合わなかった。今後の再発防止策を求められている。"),
                ("ログインできない", "パスワードを忘れてしまい管理画面に入れない。"),
                ("見積の再送", "先日の見積書をもう一度メールで送ってほしい。"),
            };
            foreach (var (subject, body) in rows)
                await db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject, body) VALUES (@p1, @p2)", new() { ["@p1"] = subject, ["@p2"] = body });

            _design = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector" });
            m.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(m);
        }

        [TearDown]
        public void TearDown()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        [Test]
        public async Task 再索引して納期に関する似た問い合わせをAgentが探す()
        {
            var indexer = new SemanticSearchIndexer(_embedding);
            await using var db = new DbAccessor(_dataSources);
            var io = new IndexingModuleDataIO(_design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>()), indexer);
            Assert.That(await SemanticSearchIndexer.ReindexAsync(io, _design, "Inquiry"), Is.EqualTo(5));
            await db.CommitAsync();

            var entries = await SemanticSearchIndexReader.ReadAsync(db, _design.Modules.Find("Inquiry")!, _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single(), CancellationToken.None);
            Assert.That(entries.Count, Is.EqualTo(5));

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
