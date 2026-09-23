using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// 再索引 (SemanticSearchField のスクリプト Reindex のサーバー側): まとめて埋め込む・missingOnly・ジョブの進捗と確定・中断・入口検査・同じモジュールの合流。
    /// 実 DB は SQLite (索引付けは DB を選ばない。検索だけが pgvector / SQL Server 2025 前提)。
    /// </summary>
    public class SemanticSearchReindexJobStoreTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = Array.Empty<DataSource>();
        DesignData _design = null!;
        FakeEmbeddingGenerator _embedding = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

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

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            m.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(m);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"semantic_reindex_{Guid.NewGuid():N}.db");
            _dataSources = [new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }];
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, search_text TEXT, search_vector TEXT)", new());
            for (var i = 1; i <= 7; i++)
                await db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject) VALUES (@p1)", new() { ["@p1"] = $"件名 {i}" });
            //3 と 5 は索引済み扱い (ベクトルあり)
            foreach (var id in new[] { 3, 5 })
                await db.ExecuteAsync(Ds, "UPDATE inquiries SET search_text = 'old', search_vector = @p1 WHERE id = @p2", new() { ["@p1"] = "[0.1,0.2]", ["@p2"] = id });
            _design = CreateDesign();
            _embedding = new FakeEmbeddingGenerator();
        }

        [TearDown]
        public void TearDown()
        {
            _embedding?.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        SemanticSearchIndexer Indexer() => new(() => _embedding);

        SemanticSearchReindexScope OpenScope(SemanticSearchIndexer indexer)
        {
            var db = new DbAccessor(_dataSources);
            return new SemanticSearchReindexScope(new IndexingModuleDataIO(_design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>()), indexer), db);
        }

        async Task<List<(int Id, string? Text, string? Vector)>> RowsAsync()
        {
            await using var db = new DbAccessor(_dataSources);
            return (await db.QueryAsync(Ds, "SELECT id, search_text, search_vector FROM inquiries ORDER BY id", new()))
                .Select(r => (Convert.ToInt32(r["id"]), r["search_text"] as string, r["search_vector"] as string)).ToList();
        }

        static async Task<SemanticSearchReindexStatusResponse> WaitDoneAsync(SemanticSearchReindexJobStore store, string owner, string id, int timeoutMs = 10000)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                var s = store.GetStatus(owner, id);
                Assert.That(s, Is.Not.Null);
                if (!s!.IsRunning) return s;
                await Task.Delay(20);
            }
            throw new TimeoutException();
        }

        [Test]
        public async Task 全行をページ単位でまとめて埋め込み文章とベクトルを書き直す()
        {
            var indexer = Indexer();
            await using var scope = OpenScope(indexer);
            var progress = new List<(int, int)>();
            var count = await indexer.ReindexAsync(scope.ModuleDataIO, scope.DbAccessor, _design, "Inquiry", progress: new SyncProgress(progress), pageSize: 3);
            Assert.That(count, Is.EqualTo(7));
            Assert.That(_embedding.Inputs.Count, Is.EqualTo(7));
            Assert.That(_embedding.Calls, Is.EqualTo(3), "7 行を 3 ページ = 埋め込み呼び出し 3 回 (行ごとではない)");
            var rows = await RowsAsync();
            Assert.That(rows.Select(r => r.Text), Is.EqualTo(Enumerable.Range(1, 7).Select(i => $"件名: 件名 {i}")));
            Assert.That(rows.All(r => r.Vector == SemanticSearchVector.Encode(FakeEmbeddingGenerator.Embed(r.Text!))), Is.True, "ApplyAsync は付いてきたベクトルをそのまま使う");
            Assert.That(progress.First(), Is.EqualTo((0, 7)));
            Assert.That(progress.Last(), Is.EqualTo((7, 7)));
        }

        [Test]
        public async Task missingOnlyはベクトルの無い行だけ書き直す()
        {
            var indexer = Indexer();
            await using var scope = OpenScope(indexer);
            var progress = new List<(int, int)>();
            var count = await indexer.ReindexAsync(scope.ModuleDataIO, scope.DbAccessor, _design, "Inquiry", missingOnly: true, progress: new SyncProgress(progress), pageSize: 100);
            Assert.That(count, Is.EqualTo(5));
            var rows = await RowsAsync();
            Assert.That(rows.Where(r => r.Id is 3 or 5).Select(r => (r.Text, r.Vector)), Is.All.EqualTo(("old", "[0.1,0.2]")), "索引済みの行は触らない");
            Assert.That(rows.Where(r => r.Id is not (3 or 5)).All(r => r.Text == $"件名: 件名 {r.Id}" && r.Vector != null), Is.True);
            Assert.That(progress.First(), Is.EqualTo((0, 5)));
        }

        [Test]
        public async Task 埋め込みモデルが無ければ文章だけ書き直す()
        {
            var indexer = new SemanticSearchIndexer(null);
            await using var scope = OpenScope(indexer);
            Assert.That(await indexer.ReindexAsync(scope.ModuleDataIO, scope.DbAccessor, _design, "Inquiry"), Is.EqualTo(7));
            var rows = await RowsAsync();
            Assert.That(rows.All(r => r.Text == $"件名: 件名 {r.Id}" && r.Vector == null), Is.True);
        }

        [Test]
        public void 埋め込みの失敗は例外になる()
        {
            var indexer = Indexer();
            _embedding.Fail = true;
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var scope = OpenScope(indexer);
                await indexer.ReindexAsync(scope.ModuleDataIO, scope.DbAccessor, _design, "Inquiry");
            });
        }

        [Test]
        public async Task ジョブは即requestIdを返しポーリングで進捗と確定が見える()
        {
            var indexer = Indexer();
            using var store = new SemanticSearchReindexJobStore(indexer, () => _design, new SemanticSearchReindexJobStoreOptions { PageSize = 2 });
            var id = store.Start("user1", "Inquiry", missingOnly: false, () => OpenScope(indexer));
            Assert.That(id, Is.Not.Empty);
            var done = await WaitDoneAsync(store, "user1", id);
            Assert.That(done.Status, Is.EqualTo(AIChatJobStatus.Done));
            Assert.That(done.Processed, Is.EqualTo(7));
            Assert.That(done.Total, Is.EqualTo(7));
            Assert.That(store.GetStatus("someone-else", id), Is.Null, "他人には見えない");
            Assert.That(store.GetStatus("user1", "nope"), Is.Null);
        }

        [Test]
        public async Task 同じモジュールが走っている間は合流し中断もできる()
        {
            var indexer = Indexer();
            _embedding.Delay = TimeSpan.FromMilliseconds(300);
            using var store = new SemanticSearchReindexJobStore(indexer, () => _design, new SemanticSearchReindexJobStoreOptions { PageSize = 1 });
            var id = store.Start("user1", "Inquiry", false, () => OpenScope(indexer));
            var joined = store.Start("user2", "Inquiry", false, () => OpenScope(indexer));
            Assert.That(joined, Is.EqualTo(id), "走っている同じモジュールのジョブに合流する");
            Assert.That(store.GetStatus("user2", id), Is.Not.Null, "合流した人にも見える");

            Assert.That(store.Cancel("user1", id), Is.True);
            var status = await WaitDoneAsync(store, "user1", id);
            Assert.That(status.Status, Is.EqualTo(AIChatJobStatus.Canceled));
            Assert.That(status.Processed, Is.LessThan(7));

            //終わったので次は新しいジョブ
            _embedding.Delay = TimeSpan.Zero;
            var next = store.Start("user1", "Inquiry", false, () => OpenScope(indexer));
            Assert.That(next, Is.Not.EqualTo(id));
            Assert.That((await WaitDoneAsync(store, "user1", next)).Status, Is.EqualTo(AIChatJobStatus.Done));
        }

        [Test]
        public async Task 入口検査は読めるSemanticSearchFieldがあるときだけ通す()
        {
            var indexer = Indexer();
            using var store = new SemanticSearchReindexJobStore(indexer, () => _design);
            await using var scope = OpenScope(indexer);
            var id = await store.StartAsync("u", new SemanticSearchReindexRequest { ModuleName = "Inquiry", FieldName = "Search" }, scope.ModuleDataIO, () => OpenScope(indexer));
            Assert.That((await WaitDoneAsync(store, "u", id)).Status, Is.EqualTo(AIChatJobStatus.Done));

            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("u", new SemanticSearchReindexRequest { ModuleName = "Inquiry", FieldName = "Subject" }, scope.ModuleDataIO, () => OpenScope(indexer)), "型が違う");
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("u", new SemanticSearchReindexRequest { ModuleName = "Nope", FieldName = "Search" }, scope.ModuleDataIO, () => OpenScope(indexer)));
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("u", new SemanticSearchReindexRequest { ModuleName = "Inquiry", FieldName = "Nope" }, scope.ModuleDataIO, () => OpenScope(indexer)));
        }

        sealed class SyncProgress(List<(int, int)> log) : IProgress<(int Processed, int Total)>
        {
            public void Report((int Processed, int Total) value) { lock (log) log.Add(value); }
        }
    }
}
