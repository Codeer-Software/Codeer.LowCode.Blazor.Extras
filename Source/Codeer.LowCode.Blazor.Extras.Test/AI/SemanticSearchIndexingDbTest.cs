using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Extras.Test.Tag;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// SemanticSearchService (保存時の索引付け): 実 DB (SQLite) で、保存のたびにサーバーが保存した行から文章とベクトルを作って書き込み専用列に書くこと、
    /// 埋め込み失敗時は文章だけ入ること、再索引、デザインチェック。索引付けは DB を選ばない (検索だけが pgvector / SQL Server 2025 前提)。
    /// </summary>
    public class SemanticSearchIndexingDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        FakeEmbeddingProvider _embedding = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        //テンプレートの CustomizedModuleDataIO と同じ結線 (インターセプタの登録)
        sealed class IndexingModuleDataIO : ModuleDataIO
        {
            public IndexingModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchService semanticSearch)
                : base(design, auth, db, files) => AddInterceptor(semanticSearch.Interceptor);
        }

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body" });
            m.Fields.Add(new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "is_deleted" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            m.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(m);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"semantic_search_test_{Guid.NewGuid():N}.db");
            _db = NewDb();
            await _db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, body TEXT, is_deleted INTEGER DEFAULT 0, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject, body) VALUES ('納期遅れの相談', '注文がまだ届かない'), ('請求書の再発行', '宛名を変更して再発行してほしい'), ('納品書の再発行', '納品書を紛失した')", new());
            _design = CreateDesign();
            _embedding = new FakeEmbeddingProvider();
        }

        DbAccessor NewDb() => new([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        SemanticSearchService Indexer(bool withEmbedding = true) => new(withEmbedding ? () => _embedding : () => null, () => _design);
        ModuleDataIO CreateIO(SemanticSearchService indexer) => new IndexingModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), indexer);

        async Task<(string? Text, string? Vector)> RowAsync(string id)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT search_text, search_vector FROM inquiries WHERE id = {id}", new())).Single();
            var values = row.Values.ToList();
            return (values[0] is null or DBNull ? null : values[0].ToString(), values[1] is null or DBNull ? null : values[1].ToString());
        }

        //本文だけを変える更新 (クライアントの Submit と同じで、このフィールドのデータは送らない)
        static ModuleSubmitData UpdateSubmit(string id, SemanticSearchFieldData? sent = null)
        {
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Body"] = new TextFieldData { Value = "本文を変更" };
            if (sent != null) data.Fields["Search"] = sent;
            return new ModuleSubmitData { ModuleName = "Inquiry", Id = id, Update = [data] };
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        [Test]
        public async Task 更新すると保存した行からサーバーが文章とベクトルを作って書き込み専用列へ保存する()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));
            var (text, vector) = await RowAsync("1");
            Assert.That(text, Is.EqualTo("件名: 納期遅れの相談\n本文: 本文を変更"), "送られていない件名も保存した行から入る");
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(text!))));
            Assert.That(_embedding.Inputs, Is.EqualTo(new[] { text }));
            //他の行は触らない
            Assert.That(await RowAsync("2"), Is.EqualTo(((string?)null, (string?)null)));
        }

        [Test]
        public async Task 送信データに文章とベクトルがあればそのまま書く_再索引()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1", new SemanticSearchFieldData { Text = "t", Vector = "[1,2]" })]));
            Assert.That(await RowAsync("1"), Is.EqualTo(("t", "[1,2]")));
            Assert.That(_embedding.Inputs, Is.Empty, "埋め込みを呼び直さない");
        }

        [Test]
        public async Task 新規行も採番された後に保存した行から作る()
        {
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = tempId;
            data.Fields["Subject"] = new TextFieldData { Value = "新しい件名" };
            data.Fields["Body"] = new TextFieldData { Value = "新しい本文" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = tempId.Value!, Add = [data] }]));
            var (text, vector) = await RowAsync("4");
            Assert.That(text, Is.EqualTo("件名: 新しい件名\n本文: 新しい本文"));
            Assert.That(vector, Is.Not.Null);
        }

        [Test]
        public async Task 一括INSERT用のApplyAsyncは取り込む行のデータから作る()
        {
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Subject"] = new TextFieldData { Value = "一括取込の件名" };
            await Indexer().ApplyAsync(data, isNewData: true);
            var sent = (SemanticSearchFieldData)data.Fields["Search"]!;
            Assert.That(sent.Text, Is.EqualTo("件名: 一括取込の件名"));
            Assert.That(sent.Vector, Is.Null, "一括取込は文章だけ (ベクトルは ReindexMissing)");

            //すでにあれば触らない・更新では何もしない (保存後の行から作る)
            await Indexer().ApplyAsync(data, isNewData: true);
            Assert.That(data.Fields["Search"], Is.SameAs(sent));
            var update = new ModuleData { Name = "Inquiry" };
            update.Fields["Subject"] = new TextFieldData { Value = "x" };
            await Indexer().ApplyAsync(update, isNewData: false);
            Assert.That(update.Fields.ContainsKey("Search"), Is.False);
        }

        [Test]
        public async Task 埋め込みモデルが無い又は失敗なら文章だけ保存する()
        {
            AssertNoError(await CreateIO(Indexer(withEmbedding: false)).SubmitWithTransactionAsync([UpdateSubmit("1")]));
            Assert.That(await RowAsync("1"), Is.EqualTo(("件名: 納期遅れの相談\n本文: 本文を変更", (string?)null)));

            _embedding.Fail = true;
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("2")]));
            Assert.That(await RowAsync("2"), Is.EqualTo(("件名: 請求書の再発行\n本文: 本文を変更", (string?)null)));
        }

        [Test]
        public async Task 埋め込みのタイムアウトでも保存は止めず文章だけ保存する()
        {
            //HttpClient / SDK のタイムアウトは中断例外で来る。保存の中断ではないので失敗扱い (文章だけ)
            _embedding.Throw = new TaskCanceledException("timeout (fake)");
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));
            Assert.That(await RowAsync("1"), Is.EqualTo(("件名: 納期遅れの相談\n本文: 本文を変更", (string?)null)));

            //上限時間を超えても同じ
            _embedding.Throw = null;
            _embedding.Delay = TimeSpan.FromSeconds(2);
            var slow = new SemanticSearchService(() => _embedding, () => _design) { SaveEmbeddingTimeout = TimeSpan.FromMilliseconds(100) };
            AssertNoError(await CreateIO(slow).SubmitWithTransactionAsync([UpdateSubmit("2")]));
            Assert.That(await RowAsync("2"), Is.EqualTo(("件名: 請求書の再発行\n本文: 本文を変更", (string?)null)));
        }

        [Test]
        public async Task 一つの送信にId無しの新規行が複数あっても全部索引される()
        {
            var adds = new[] { "複数 A", "複数 B" }.Select(s => { var d = new ModuleData { Name = "Inquiry" }; d.Fields["Subject"] = new TextFieldData { Value = s }; return d; }).ToList();
            var results = await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "", Add = adds, NoTemporaryIdResolution = true }]);
            AssertNoError(results);
            Assert.That((await RowAsync("4")).Text, Is.EqualTo("件名: 複数 A"));
            Assert.That((await RowAsync("5")).Text, Is.EqualTo("件名: 複数 B"));
        }

        [Test]
        public async Task 再索引は起こした人が読めない項目も文章に入れる()
        {
            //件名を管理者だけが読める項目にする。U1 は管理者ではないが、再索引の文章には件名が入る (保存時と同じ = 誰が起こしても同じ文章)
            await _db.ExecuteAsync(Ds, "CREATE TABLE users (id TEXT PRIMARY KEY, is_admin INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO users (id, is_admin) VALUES ('U1', 0)", new());
            var user = new ModuleDesign { Name = "User", DataSourceName = Ds, DbTable = "users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsAdmin", DbColumn = "is_admin" });
            _design.AddModule(user);
            _design.AppSettings.CurrentUserModuleDesignName = "User";
            var rule = new PermissionFieldDesign { Name = "P", TargetFields = { "Subject" } };
            rule.ReadCondition.ModuleName = "Inquiry";
            rule.ReadCondition.Condition = new FieldValueMatchCondition { SearchTargetVariable = "CurrentUser.IsAdmin.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(true) };
            _design.Modules.Find("Inquiry")!.Fields.Add(rule);

            var indexer = Indexer();
            Assert.That(await indexer.ReindexAsync(CreateIO(indexer), _db, "Inquiry"), Is.EqualTo(3));
            Assert.That((await RowAsync("1")).Text, Is.EqualTo("件名: 納期遅れの相談\n本文: 注文がまだ届かない"));
        }

        [Test]
        public async Task 索引の項目を読めない人が更新しても索引は保存した行から作られる()
        {
            //このフィールドに読み取り条件 (管理者だけ)。U1 は管理者ではない = 読めない (= 書けない) が、本文の更新で索引は最新になる
            await _db.ExecuteAsync(Ds, "CREATE TABLE users (id TEXT PRIMARY KEY, is_admin INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO users (id, is_admin) VALUES ('U1', 0)", new());
            var user = new ModuleDesign { Name = "User", DataSourceName = Ds, DbTable = "users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsAdmin", DbColumn = "is_admin" });
            _design.AddModule(user);
            _design.AppSettings.CurrentUserModuleDesignName = "User";
            var rule = new PermissionFieldDesign { Name = "P", TargetFields = { "Search" } };
            rule.ReadCondition.ModuleName = "Inquiry";
            rule.ReadCondition.Condition = new FieldValueMatchCondition { SearchTargetVariable = "CurrentUser.IsAdmin.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(true) };
            _design.Modules.Find("Inquiry")!.Fields.Add(rule);

            var io = CreateIO(Indexer());
            var keyword = new SearchCondition("Inquiry") { Condition = new SemanticMatchCondition { FieldName = "Search", Keywords = "本文" } };
            Assert.ThrowsAsync<LowCodeAccessDeniedException>(async () => await io.GetListAsync(keyword, 0), "読めないので検索はできない");

            AssertNoError(await io.SubmitWithTransactionAsync([UpdateSubmit("1")]));
            Assert.That((await RowAsync("1")).Text, Is.EqualTo("件名: 納期遅れの相談\n本文: 本文を変更"));
        }

        [Test]
        public async Task タグだけを変えた保存でも主レコードの文章が作り直される()
        {
            //タグ付け行の追加は子モジュール (ContactTags) の Add だけで、主レコード (Contact) は Update に載らない
            await TagTestDesigns.CreateTablesAsync(_db);
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_text TEXT", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_vector TEXT", new());
            _design = TagTestDesigns.Create();
            _design.Modules.Find("Contact")!.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Name", "Tags" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });

            var tag = new ModuleData { Name = "ContactTags" };
            tag.Fields["Id"] = IdFieldData.NewId();
            tag.Fields["OwnerId"] = new IdFieldData { Value = "2" };
            tag.Fields["Name"] = new TextFieldData { Value = "DXPO" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Contact", Id = "2", Add = [tag] }]));
            var row = (await _db.QueryAsync(Ds, "SELECT search_text FROM contacts WHERE id = 2", new())).Single();
            Assert.That(row.Values.Single()?.ToString(), Is.EqualTo("名前: B\nタグ: 展示会, DXPO"));
        }

        [Test]
        public async Task 再索引はタグ付けモジュールに閲覧条件があってもタグ名を文章に入れる()
        {
            //タグ付けモジュール (ContactTags) を管理者だけが読める構成。保存時と同じ内部読みで再索引するので、U1 (管理者ではない) が起こしてもタグが入る
            await TagTestDesigns.CreateTablesAsync(_db);
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_text TEXT", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_vector TEXT", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE users (id TEXT PRIMARY KEY, is_admin INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO users (id, is_admin) VALUES ('U1', 0)", new());
            _design = TagTestDesigns.Create();
            _design.Modules.Find("Contact")!.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Name", "Tags" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            var user = new ModuleDesign { Name = "User", DataSourceName = Ds, DbTable = "users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsAdmin", DbColumn = "is_admin" });
            _design.AddModule(user);
            _design.AppSettings.CurrentUserModuleDesignName = "User";
            _design.Modules.Find("ContactTags")!.UserReadCondition = new ModuleMatchCondition
            {
                ModuleName = "ContactTags",
                Condition = new FieldValueMatchCondition { SearchTargetVariable = "CurrentUser.IsAdmin.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(true) },
            };

            var indexer = Indexer();
            Assert.That(await indexer.ReindexAsync(CreateIO(indexer), _db, "Contact"), Is.EqualTo(4));
            var row = (await _db.QueryAsync(Ds, "SELECT search_text FROM contacts WHERE id = 1", new())).Single();
            Assert.That(row.Values.Single()?.ToString(), Is.EqualTo("名前: A\nタグ: 展示会, DXPO"));
        }

        [Test]
        public async Task ファイル取込の新規行は一括INSERTに乗らなくても索引される()
        {
            //取込の行は Id が空のまま Add に載る (NoTemporaryIdResolution)。1 行ずつの経路でも採番後の行から作る
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Subject"] = new TextFieldData { Value = "取込の件名" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "", Add = [data], NoTemporaryIdResolution = true }]));
            Assert.That((await RowAsync("4")).Text, Is.EqualTo("件名: 取込の件名"));
        }

        [Test]
        public async Task Idを手打ちで変えた更新は新しいIdの行を索引する()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE memos (id TEXT PRIMARY KEY, title TEXT, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO memos (id, title) VALUES ('A', 'a')", new());
            var memo = new ModuleDesign { Name = "Memo", DataSourceName = Ds, DbTable = "memos", CanUpdate = true };
            memo.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id", IsManualInput = true });
            memo.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            memo.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            memo.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(memo);

            var data = new ModuleData { Name = "Memo" };
            data.Fields["Id"] = new IdFieldData { Value = "@replace:A->B" };
            data.Fields["Title"] = new TextFieldData { Value = "b" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Memo", Id = "A", Update = [data] }]));
            var rows = await _db.QueryAsync(Ds, "SELECT id, search_text FROM memos", new());
            Assert.That(rows.Select(r => (r["id"]?.ToString(), r["search_text"]?.ToString())), Is.EqualTo(new[] { ("B", "題名: b") }));
        }

        [Test]
        public async Task 索引の書き込みはレコードの更新ではない_楽観ロックの版は保存の分しか進まず再索引でも壊れない()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE tickets (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, version INTEGER, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO tickets (title, version) VALUES ('a', 5)", new());
            var ticket = new ModuleDesign { Name = "Ticket", DataSourceName = Ds, DbTable = "tickets", CanUpdate = true };
            ticket.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            ticket.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            ticket.Fields.Add(new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = "version", IncrementVersion = true });
            ticket.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            ticket.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(ticket);
            async Task<(string? Title, long Version, string? Text)> TicketAsync()
            {
                var r = (await _db.QueryAsync(Ds, "SELECT title, version, search_text FROM tickets WHERE id = 1", new())).Single();
                return (r["title"]?.ToString(), Convert.ToInt64(r["version"]), r["search_text"] is null or DBNull ? null : r["search_text"].ToString());
            }

            var data = new ModuleData { Name = "Ticket" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = "b" };
            data.Fields[SystemFieldNames.OptimisticLocking] = new OptimisticLockingFieldData { Value = MultiTypeValue.Create(5L) };
            var io = CreateIO(Indexer());
            AssertNoError(await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Ticket", Id = "1", Update = [data] }]));
            Assert.That(await TicketAsync(), Is.EqualTo(("b", 6L, "題名: b")), "保存で 1 つ進むだけ");

            //再索引も書き込み専用列だけを書く (版は進まない)
            Assert.That(await Indexer().ReindexAsync(io, _db, "Ticket"), Is.EqualTo(1));
            Assert.That(await TicketAsync(), Is.EqualTo(("b", 6L, "題名: b")));
        }

        [Test]
        public async Task 対象フィールドに変更の無い更新は索引を作り直さない()
        {
            //件名だけを対象にする。本文の更新では読み直しも埋め込みも起きない
            _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single().SourceFields.Add("Subject");
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));
            Assert.That(await RowAsync("1"), Is.EqualTo(((string?)null, (string?)null)));
            Assert.That(_embedding.Inputs, Is.Empty);

            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Subject"] = new TextFieldData { Value = "件名を変更" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "1", Update = [data] }]));
            Assert.That((await RowAsync("1")).Text, Is.EqualTo("件名: 件名を変更"));
        }

        [Test]
        public async Task 一括INSERTの取込は文章だけ付ける_仮Idは応答に出さない()
        {
            //ApplyAsync(datas) = 文章だけ (ベクトルは取込後の ReindexMissing。保存の中で大量の埋め込みを待たない)
            var rows = new[] { "一括 1", "一括 2", "一括 3" }.Select(s => { var d = new ModuleData { Name = "Inquiry" }; d.Fields["Subject"] = new TextFieldData { Value = s }; return d; }).ToList();
            await Indexer().ApplyAsync(rows);
            Assert.That(rows.Select(r => ((SemanticSearchFieldData)r.Fields["Search"]!).Text), Is.EqualTo(new[] { "件名: 一括 1", "件名: 一括 2", "件名: 一括 3" }));
            Assert.That(rows.All(r => ((SemanticSearchFieldData)r.Fields["Search"]!).Vector == null), Is.True);
            Assert.That(_embedding.Calls, Is.EqualTo(0));

            //一括 INSERT の経路 (閾値以上の純 Add・NoTemporaryIdResolution): 主レコードに付けた仮 Id が結果に残らない
            var saved = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 2;
            try
            {
                var adds = new[] { "取込 A", "取込 B" }.Select(s => { var d = new ModuleData { Name = "Inquiry" }; d.Fields["Subject"] = new TextFieldData { Value = s }; return d; }).ToList();
                var results = await CreateIO(Indexer()).SubmitWithTransactionAsync(adds.Select(d => new ModuleSubmitData { ModuleName = "Inquiry", Id = "", Add = [d], NoTemporaryIdResolution = true }).ToList());
                AssertNoError(results);
                Assert.That(results.Select(r => (r.SourceId, r.DestinationId)), Is.All.EqualTo(("", "")));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = saved;
            }
        }

        [Test]
        public async Task 再索引は更新できないモジュールでもでき_更新日時も変えない()
        {
            await _db.ExecuteAsync(Ds, "ALTER TABLE inquiries ADD COLUMN updated_at TEXT", new());
            await _db.ExecuteAsync(Ds, "UPDATE inquiries SET updated_at = '2020-01-01 00:00:00'", new());
            var module = _design.Modules.Find("Inquiry")!;
            module.CanUpdate = false;
            module.Fields.Add(new DateTimeFieldDesign { Name = SystemFieldNames.UpdatedAt, DbColumn = "updated_at" });
            var indexer = Indexer();
            Assert.That(await indexer.ReindexAsync(CreateIO(indexer), _db, "Inquiry"), Is.EqualTo(3));
            var rows = await _db.QueryAsync(Ds, "SELECT updated_at, search_text FROM inquiries ORDER BY id", new());
            Assert.That(rows.Select(r => r["updated_at"]?.ToString()), Is.All.EqualTo("2020-01-01 00:00:00"));
            Assert.That(rows.Select(r => r["search_text"]), Is.All.Not.Null);
        }

        [Test]
        public async Task 索引の書き込みに失敗したら保存も失敗する()
        {
            //ベクトル列に入らない (埋め込みモデルを変えて列を作り直していない状態の代わりに CHECK 制約) → 索引の UPDATE が失敗 = 保存も失敗 (DB の失敗は同じトランザクションなので握らない)
            await _db.ExecuteAsync(Ds, "CREATE TABLE notes (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, search_text TEXT, search_vector TEXT CHECK (search_vector IS NULL))", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO notes (title) VALUES ('a')", new());
            var note = new ModuleDesign { Name = "Note", DataSourceName = Ds, DbTable = "notes", CanUpdate = true };
            note.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            note.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            note.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            note.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(note);
            //DbAccessor は StartTransaction の後に開いた接続にだけトランザクションを張る (準備で開いたままの接続で保存すると自動コミットになり、ロールバックが観測できない)。
            //ホストと同じく保存は新しい DbAccessor (= リクエストごと) で行う
            await _db.DisposeAsync();
            _db = NewDb();
            var data = new ModuleData { Name = "Note" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = "b" };
            var results = await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Note", Id = "1", Update = [data] }]);
            Assert.That(results.Single().ExceptionMessage, Is.Not.Empty);
            var row = (await _db.QueryAsync(Ds, "SELECT title, search_text FROM notes WHERE id = 1", new())).Single();
            Assert.That(row["title"]?.ToString(), Is.EqualTo("a"), "保存は戻る");
        }

        [Test]
        public async Task 文章だけが送信に入っている新規行は読み直して埋め込む_旧結線()
        {
            //0.20 までのテンプレ (AddAsync で ApplyAsync) が残っていても新規行にベクトルが付く
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = tempId;
            data.Fields["Subject"] = new TextFieldData { Value = "旧結線" };
            data.Fields["Search"] = new SemanticSearchFieldData { Text = "件名: 送信データの文章", Vector = null };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = tempId.Value!, Add = [data] }]));
            var (text, vector) = await RowAsync("4");
            Assert.That(text, Is.EqualTo("件名: 旧結線"), "保存した行から作り直す");
            Assert.That(vector, Is.Not.Null);
        }

        [Test]
        public async Task 複合Idのモジュールも再索引できる()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE pairs (code TEXT NOT NULL, seq INTEGER NOT NULL, title TEXT, search_text TEXT, search_vector TEXT, PRIMARY KEY (code, seq))", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO pairs (code, seq, title) VALUES ('A', 1, 'a1'), ('A', 2, 'a2'), ('B', 1, 'b1')", new());
            var pair = new ModuleDesign { Name = "Pair", DataSourceName = Ds, DbTable = "pairs" };
            pair.Fields.Add(new IdFieldDesign { Name = "Id", CompositeIdVariables = { "Code.Value", "Seq.Value" }, CompositeIdSeparator = "-" });
            pair.Fields.Add(new TextFieldDesign { Name = "Code", DbColumn = "code" });
            pair.Fields.Add(new NumberFieldDesign { Name = "Seq", DbColumn = "seq" });
            pair.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            pair.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Title" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            pair.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(pair);
            var indexer = Indexer();
            Assert.That(await indexer.ReindexAsync(CreateIO(indexer), _db, "Pair", pageSize: 2), Is.EqualTo(3));
            var rows = await _db.QueryAsync(Ds, "SELECT code, seq, search_text FROM pairs ORDER BY code, seq", new());
            Assert.That(rows.Select(r => r["search_text"]?.ToString()), Is.EqualTo(new[] { "題名: a1", "題名: a2", "題名: b1" }));
            //missingOnly は複合 Id では判定できず全行
            Assert.That(await indexer.ReindexAsync(CreateIO(indexer), _db, "Pair", missingOnly: true), Is.EqualTo(3));
        }

        [Test]
        public async Task 削除した行は索引しない()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Inquiry", Id = "1" }] }]));
            Assert.That(await RowAsync("1"), Is.EqualTo(((string?)null, (string?)null)));
            Assert.That(_embedding.Inputs, Is.Empty);
        }

        [Test]
        public async Task 再索引は読める全行の文章を組み立てて保存する()
        {
            await _db.ExecuteAsync(Ds, "UPDATE inquiries SET is_deleted = 1 WHERE id = 3", new());
            var indexer = Indexer();
            var io = CreateIO(indexer);
            var count = await indexer.ReindexAsync(io, _db, "Inquiry", pageSize: 1);
            Assert.That(count, Is.EqualTo(2), "論理削除の行は読み込みに出ないので索引しない");
            Assert.That((await RowAsync("2")).Text, Is.EqualTo("件名: 請求書の再発行\n本文: 宛名を変更して再発行してほしい"));
            Assert.That((await RowAsync("1")).Vector, Is.Not.Null);
            Assert.That((await RowAsync("3")).Vector, Is.Null);
            Assert.That(_embedding.Inputs.Count, Is.EqualTo(2), "保存時の索引付けが再索引の行を埋め込み直すことはない");
        }

        [Test]
        public async Task 書き込み専用列は読み込みで来ない()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));
            var page = await CreateIO(Indexer()).GetListAsync(new SearchCondition { ModuleName = "Inquiry" }, 0);
            var data = page.Items.First().Fields.TryGetValue("Search", out var d) ? d as SemanticSearchFieldData : null;
            Assert.That(data?.Text, Is.Null);
            Assert.That(data?.Vector, Is.Null);
        }

        [Test]
        public void ベクトルの保存形式()
        {
            var encoded = SemanticSearchVector.Encode(new float[] { 1, 0, 0.5f });
            Assert.That(encoded, Does.StartWith("[").And.EndWith("]").And.Not.Contain(" "), "JSON 配列風テキスト (pgvector / SQL Server の VECTOR がそのままキャストできる形)");
            Assert.That(SemanticSearchVector.Encode(new float[] { 1, -0.5f, 0.25f }), Is.EqualTo("[1,-0.5,0.25]"));
        }

        [Test]
        public void デザインチェックは列の未設定と存在しないフィールドを指摘する()
        {
            var field = _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single();
            Assert.That(CheckCodes(), Is.Empty);

            var code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), 1 /* ColumnsRequired */);
            field.DbColumnVector = "";
            Assert.That(CheckCodes(), Does.Contain(code));
            field.DbColumnVector = "search_vector";
            field.DbColumnText = "";
            Assert.That(CheckCodes(), Does.Contain(code));
            field.DbColumnText = "search_text";
            field.DbColumnVectorSearch = "";
            Assert.That(CheckCodes(), Does.Contain(code), "ベクトル検索用の列も必須");
            field.DbColumnVectorSearch = "search_vector";

            field.SourceFields.Add("NoSuchField");
            var codes = CheckCodes();
            Assert.That(codes, Does.Not.Contain(code));
            Assert.That(codes.Count, Is.EqualTo(1), string.Join(", ", codes));
        }

        List<string> CheckCodes()
        {
            var field = _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single();
            return field.CheckDesign(new DesignCheckContext("Inquiry", _design, Utilities.CreateDataSource())).Select(e => e.Code).ToList();
        }
    }
}
