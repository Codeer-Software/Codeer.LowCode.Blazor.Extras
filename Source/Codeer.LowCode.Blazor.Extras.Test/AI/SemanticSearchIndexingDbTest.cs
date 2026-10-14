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
        public async Task 送信データに文章とベクトルが付いていても保存した行から作り直す()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1", new SemanticSearchFieldData { Text = "t", Vector = "[1,2]" })]));
            var (text, vector) = await RowAsync("1");
            Assert.That(text, Is.EqualTo("件名: 納期遅れの相談\n本文: 本文を変更"), "送られた文章は使わない");
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(text!))), "送られたベクトルは使わない");
            Assert.That(_embedding.Inputs, Is.EqualTo(new[] { text }), "保存した行の文章で埋め込む");
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
#pragma warning disable CS0618 //旧形式 (1 行版) の挙動の確認
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
#pragma warning restore CS0618
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
        public async Task 一覧の中の行のタグだけを変えてもその行の索引が作り直される()
        {
            //送信の主レコードは会社 (Company)。タグ付け行 (ContactTags) は会社の一覧の中の社員 (Contact 2) のもの = 作り直すのは送信の主レコードでなくタグの持ち主
            await TagTestDesigns.CreateTablesAsync(_db);
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_text TEXT", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_vector TEXT", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO companies (name) VALUES ('X社')", new());
            await _db.ExecuteAsync(Ds, "UPDATE contacts SET company_id = 1 WHERE id = 2", new());
            _design = TagTestDesigns.Create();
            TagTestDesigns.AddCompany(_design);
            _design.Modules.Find("Contact")!.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Name", "Tags" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });

            //追加だけ
            var tag = new ModuleData { Name = "ContactTags" };
            tag.Fields["Id"] = IdFieldData.NewId();
            tag.Fields["OwnerId"] = new IdFieldData { Value = "2" };
            tag.Fields["Name"] = new TextFieldData { Value = "DXPO" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Company", Id = "1", Add = [tag] }]));
            Assert.That(await ContactTextAsync("2"), Is.EqualTo("名前: B\nタグ: 展示会, DXPO"));
            Assert.That(await ContactTextAsync("1"), Is.Null, "他の社員は触らない");

            //削除だけ (消す行の持ち主は保存の前に読んでおく)
            var tagId = (await _db.QueryAsync(Ds, "SELECT id FROM contact_tags WHERE owner_id = 2 AND name = 'DXPO'", new())).Single().Values.Single()!.ToString()!;
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Company", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "ContactTags", Id = tagId }] }]));
            Assert.That(await ContactTextAsync("2"), Is.EqualTo("名前: B\nタグ: 展示会"));
        }

        //タグのテストの準備: 社員 2・3 を会社 1 の一覧に入れ、社員に意味検索 (名前とタグ) を付ける
        async Task SetUpContactSearchAsync()
        {
            await TagTestDesigns.CreateTablesAsync(_db);
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_text TEXT", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE contacts ADD COLUMN search_vector TEXT", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO companies (name) VALUES ('X社')", new());
            await _db.ExecuteAsync(Ds, "UPDATE contacts SET company_id = 1 WHERE id IN (2, 3)", new());
            _design = TagTestDesigns.Create();
            TagTestDesigns.AddCompany(_design);
            _design.Modules.Find("Contact")!.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Name", "Tags" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
        }

        async Task<string?> ContactTextAsync(string id) => (await _db.QueryAsync(Ds, $"SELECT search_text FROM contacts WHERE id = {id}", new())).Single().Values.Single()?.ToString();

        [Test]
        public async Task 従属レコードを別の親へ付け替えると元の親と新しい親の両方を作り直す()
        {
            await SetUpContactSearchAsync();
            //社員 2 の「展示会」を社員 3 へ付け替える (送信の主レコードは会社)
            var tagId = (await _db.QueryAsync(Ds, "SELECT id FROM contact_tags WHERE owner_id = 2 AND name = '展示会'", new())).Single().Values.Single()!.ToString()!;
            var tag = new ModuleData { Name = "ContactTags" };
            tag.Fields["Id"] = new IdFieldData { Value = tagId };
            tag.Fields["OwnerId"] = new IdFieldData { Value = "3" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Company", Id = "1", Update = [tag] }]));

            Assert.That(await ContactTextAsync("2"), Does.Not.Contain("展示会"), "元の親からタグが消える");
            Assert.That(await ContactTextAsync("2"), Does.StartWith("名前: B"), "元の親も作り直す");
            Assert.That(await ContactTextAsync("3"), Does.StartWith("名前: C\nタグ: ").And.Contain("展示会").And.Contain("セミナー"), "新しい親にタグが入る");
            Assert.That(await ContactTextAsync("1"), Is.Null, "他の社員は触らない");
        }

        [Test]
        public async Task タグ付けモジュールを一括INSERTしても保存は失敗しない()
        {
            await SetUpContactSearchAsync();
            var saved = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 2;
            try
            {
                var adds = new[] { "取込タグ1", "取込タグ2" }.Select(name =>
                {
                    var d = new ModuleData { Name = "ContactTags" };
                    d.Fields["OwnerId"] = new IdFieldData { Value = "4" };
                    d.Fields["Name"] = new TextFieldData { Value = name };
                    return d;
                }).ToList();
                AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync(adds.Select(d => new ModuleSubmitData { ModuleName = "ContactTags", Id = "", Add = [d], NoTemporaryIdResolution = true }).ToList()));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = saved;
            }
            Assert.That((await _db.QueryAsync(Ds, "SELECT name FROM contact_tags WHERE owner_id = 4 ORDER BY name", new())).Select(r => r["name"]?.ToString()), Is.EqualTo(new[] { "取込タグ1", "取込タグ2" }));
            //親 (社員 4) の文章は再索引で揃う
            var indexer = Indexer();
            await indexer.ReindexAsync(CreateIO(indexer), _db, "Contact");
            Assert.That(await ContactTextAsync("4"), Is.EqualTo("名前: D\nタグ: 取込タグ1, 取込タグ2"));
        }

        [Test]
        public async Task 手入力Idのタグ付け行を一括INSERTしても親の文章は作り直さない()
        {
            //Id の分かる一括 INSERT の行でも、従属レコードの親は保存の中で作り直さない = 取込の親の文章は取込後の Reindex
            await SetUpContactSearchAsync();
            _design.Modules.Find("ContactTags")!.Fields.OfType<IdFieldDesign>().Single(f => f.Name == "Id").IsManualInput = true;
            var saved = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 2;
            try
            {
                var adds = new[] { ("100", "取込タグ1"), ("101", "取込タグ2") }.Select(e =>
                {
                    var d = new ModuleData { Name = "ContactTags" };
                    d.Fields["Id"] = new IdFieldData { Value = e.Item1 };
                    d.Fields["OwnerId"] = new IdFieldData { Value = "4" };
                    d.Fields["Name"] = new TextFieldData { Value = e.Item2 };
                    return d;
                }).ToList();
                AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync(adds.Select(d => new ModuleSubmitData { ModuleName = "ContactTags", Id = ((IdFieldData)d.Fields["Id"]).Value!, Add = [d], NoTemporaryIdResolution = true }).ToList()));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = saved;
            }
            Assert.That((await _db.QueryAsync(Ds, "SELECT id FROM contact_tags WHERE owner_id = 4 ORDER BY id", new())).Select(r => r["id"]?.ToString()), Is.EqualTo(new[] { "100", "101" }), "手入力の Id で入る");
            Assert.That(await ContactTextAsync("4"), Is.Null, "親 (社員 4) は保存の中では作り直さない");
            Assert.That(_embedding.Calls, Is.EqualTo(0), "保存の中で埋め込みを呼ばない");

            //親の文章は再索引で揃う
            var indexer = Indexer();
            await indexer.ReindexAsync(CreateIO(indexer), _db, "Contact");
            Assert.That(await ContactTextAsync("4"), Is.EqualTo("名前: D\nタグ: 取込タグ1, 取込タグ2"));
        }

        [Test]
        public async Task 束縛が複数の従属一覧は全部の束縛を満たす親だけ作り直す()
        {
            //子.Code = 親.Code AND 子.Kind = 親.Kind (Id 以外の束縛 2 つ)。Code だけだとグループ 1 と 2 の両方に当たる
            await _db.ExecuteAsync(Ds, "CREATE TABLE groups_t (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT, kind TEXT, name TEXT, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE members (id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT, kind TEXT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO groups_t (code, kind, name, search_text) VALUES ('A', 'x', 'Ax', 'stale'), ('A', 'y', 'Ay', 'stale'), ('B', 'x', 'Bx', 'stale')", new());
            var member = new ModuleDesign { Name = "Member", DataSourceName = Ds, DbTable = "members", CanCreate = true, CanUpdate = true, CanDelete = true };
            member.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            member.Fields.Add(new TextFieldDesign { Name = "Code", DbColumn = "code" });
            member.Fields.Add(new TextFieldDesign { Name = "Kind", DbColumn = "kind" });
            member.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            member.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(member);
            var group = new ModuleDesign { Name = "Group", DataSourceName = Ds, DbTable = "groups_t", CanCreate = true, CanUpdate = true, CanDelete = true };
            group.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            group.Fields.Add(new TextFieldDesign { Name = "Code", DbColumn = "code" });
            group.Fields.Add(new TextFieldDesign { Name = "Kind", DbColumn = "kind" });
            group.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "名前", DbColumn = "name" });
            group.Fields.Add(new ListFieldDesign
            {
                Name = "Members",
                CanCreate = true,
                SearchCondition = new SearchCondition
                {
                    ModuleName = "Member",
                    Condition = MultiMatchCondition.And(
                        new FieldVariableMatchCondition { SearchTargetVariable = "Code.Value", Variable = "Code.Value", Comparison = MatchComparison.Equal },
                        new FieldVariableMatchCondition { SearchTargetVariable = "Kind.Value", Variable = "Kind.Value", Comparison = MatchComparison.Equal }),
                },
            });
            group.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Name", "Members" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            group.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(group);
            async Task<string?[]> GroupTextsAsync()
                => (await _db.QueryAsync(Ds, "SELECT search_text FROM groups_t ORDER BY id", new())).Select(r => r["search_text"] is null or DBNull ? null : r["search_text"].ToString()).ToArray();

            var add = new ModuleData { Name = "Member" };
            add.Fields["Id"] = IdFieldData.NewId();
            add.Fields["Code"] = new TextFieldData { Value = "A" };
            add.Fields["Kind"] = new TextFieldData { Value = "x" };
            add.Fields["Name"] = new TextFieldData { Value = "m1" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Group", Id = "1", Add = [add] }]));
            Assert.That(await GroupTextsAsync(), Is.EqualTo(new[] { "名前: Ax", "stale", "stale" }), "両方の束縛を満たすグループ 1 だけ (Code だけ同じ 2・Kind だけ同じ 3 は触らない)");
            Assert.That(_embedding.Inputs, Is.EqualTo(new[] { "名前: Ax" }));
        }

        [Test]
        public async Task リンクの表示名が文章に入る_再索引でも同じ()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE customers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO customers (name) VALUES ('青山商事'), ('赤坂物産')", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE inquiries ADD COLUMN customer_id INTEGER", new());
            var customer = new ModuleDesign { Name = "Customer", DataSourceName = Ds, DbTable = "customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "得意先名", DbColumn = "name" });
            customer.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(customer);
            var inquiry = _design.Modules.Find("Inquiry")!;
            var link = new LinkFieldDesign { Name = "Customer", DisplayName = "得意先", DbColumn = "customer_id", ValueVariable = "Id.Value", DisplayTextVariable = "Name.Value" };
            link.SearchCondition.ModuleName = "Customer";
            inquiry.Fields.Add(link);
            var search = inquiry.Fields.OfType<SemanticSearchFieldDesign>().Single();
            search.SourceFields.Add("Subject");
            search.SourceFields.Add("Customer");

            //保存: 送るのはリンクの値 (Id) だけ
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Customer"] = new LinkFieldData { Value = "1" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "1", Update = [data] }]));
            const string expected = "件名: 納期遅れの相談\n得意先: 青山商事";
            Assert.That((await RowAsync("1")).Text, Is.EqualTo(expected), "Id ではなく表示名");

            //再索引でも同じ
            await _db.ExecuteAsync(Ds, "UPDATE inquiries SET search_text = NULL, search_vector = NULL", new());
            var indexer = Indexer();
            await indexer.ReindexAsync(CreateIO(indexer), _db, "Inquiry");
            Assert.That((await RowAsync("1")).Text, Is.EqualTo(expected));
            Assert.That((await RowAsync("2")).Text, Is.EqualTo("件名: 請求書の再発行"), "リンクが空の行は項目ごと出さない");
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

        //テンプレートの CustomizedModuleDataIO と同じく、一括 INSERT で取り込む行に ApplyAsync(datas) で文章を付ける
        sealed class BulkIndexingModuleDataIO : ModuleDataIO
        {
            readonly SemanticSearchService _semanticSearch;
            public BulkIndexingModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchService semanticSearch)
                : base(design, auth, db, files)
            {
                _semanticSearch = semanticSearch;
                AddInterceptor(semanticSearch.Interceptor);
            }
            protected override async Task BulkAddAsync(Guid transactionId, List<ModuleData> datas)
            {
                await _semanticSearch.ApplyAsync(datas);
                await base.BulkAddAsync(transactionId, datas);
            }
        }

        [Test]
        public async Task 手入力Idの一括INSERTも文章だけ付ける_埋め込みは呼ばない()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE memos (id TEXT PRIMARY KEY, title TEXT, search_text TEXT, search_vector TEXT)", new());
            var memo = new ModuleDesign { Name = "Memo", DataSourceName = Ds, DbTable = "memos", CanCreate = true };
            memo.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id", IsManualInput = true });
            memo.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            memo.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            memo.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(memo);

            var saved = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 2;
            try
            {
                var adds = new[] { ("M1", "取込 A"), ("M2", "取込 B") }.Select(e =>
                {
                    var d = new ModuleData { Name = "Memo" };
                    d.Fields["Id"] = new IdFieldData { Value = e.Item1 };
                    d.Fields["Title"] = new TextFieldData { Value = e.Item2 };
                    return d;
                }).ToList();
                var indexer = Indexer();
                var io = new BulkIndexingModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), indexer);
                var results = await io.SubmitWithTransactionAsync(adds.Select(d => new ModuleSubmitData { ModuleName = "Memo", Id = ((IdFieldData)d.Fields["Id"]).Value!, Add = [d], NoTemporaryIdResolution = true }).ToList());
                AssertNoError(results);
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = saved;
            }

            var rows = await _db.QueryAsync(Ds, "SELECT id, search_text, search_vector FROM memos ORDER BY id", new());
            Assert.That(rows.Select(r => (r["id"]?.ToString(), r["search_text"]?.ToString(), r["search_vector"] is null or DBNull)),
                Is.EqualTo(new[] { ("M1", "題名: 取込 A", true), ("M2", "題名: 取込 B", true) }), "文章だけ (ベクトルは取込後の ReindexMissing)");
            Assert.That(_embedding.Calls, Is.EqualTo(0), "保存の中で埋め込みを呼ばない");
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
            //ホストと同じく保存は新しい DbAccessor (= リクエストごと) で行う (準備で開いた接続と混ぜない)
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

        sealed class AuditedModuleDataIO : ModuleDataIO
        {
            //テンプレートと同じ順: 監査 (外側) → 意味検索 (最後に登録 = 一番内側)
            public AuditedModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchService semanticSearch)
                : base(design, auth, db, files)
            {
                AddInterceptor(new Codeer.LowCode.Blazor.Extras.Server.AuditLog.AuditIOInterceptor(design, new Codeer.LowCode.Blazor.Extras.Server.AuditLog.AuditLogDatabaseSettings()));
                AddInterceptor(semanticSearch.Interceptor);
            }
        }

        [Test]
        public async Task 索引の書き込みに失敗した保存は監査ログにも失敗として対象つきで残る()
        {
            //索引の UPDATE を CHECK 制約で失敗させる。意味検索が例外のまま投げると、本体が最外で ExceptionMessage にするので監査には成功が残っていた
            await _db.ExecuteAsync(Ds, "CREATE TABLE notes (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, search_text TEXT, search_vector TEXT CHECK (search_vector IS NULL))", new());
            var note = new ModuleDesign { Name = "Note", DataSourceName = Ds, DbTable = "notes", CanCreate = true };
            note.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            note.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            note.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            note.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(note);
            await _db.DisposeAsync();
            _db = NewDb();

            var audit = new Codeer.LowCode.Blazor.Extras.Server.AuditLog.AuditContext();
            Codeer.LowCode.Blazor.Extras.Server.AuditLog.AuditContext.Current = audit;
            try
            {
                var tempId = IdFieldData.NewId();
                var data = new ModuleData { Name = "Note" };
                data.Fields["Id"] = tempId;
                data.Fields["Title"] = new TextFieldData { Value = "新規" };
                var io = new AuditedModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), Indexer());
                var results = await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Note", Id = tempId.Value!, Add = [data] }]);

                Assert.That(results.Single().ExceptionMessage, Is.Not.Empty);
                var count = (await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM notes", new())).Single()["c"];
                Assert.That(Convert.ToInt32(count), Is.EqualTo(0), "保存は戻る");
                Assert.That(audit.Event.Result, Is.EqualTo(Codeer.LowCode.Blazor.Extras.Server.AuditLog.AuditResult.Failure));
                Assert.That(audit.Event.Detail, Is.Not.Empty);
                Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Note", "Add") }));
            }
            finally
            {
                Codeer.LowCode.Blazor.Extras.Server.AuditLog.AuditContext.Current = null;
            }
        }

        //外側のインターセプタから見える形を記録する (next() が投げたか・結果で返したか)
        sealed class OuterObserver : IModuleDataIOInterceptor
        {
            public Exception? Thrown { get; private set; }
            public List<ModuleSubmitResult>? Results { get; private set; }
            public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
            {
                try { return Results = await next(); }
                catch (Exception ex) { Thrown = ex; throw; }
            }
            public Task GetListAsync(ModuleDataIO io, SearchCondition condition, Codeer.LowCode.Blazor.Utils.Paging<ModuleData> result) => Task.CompletedTask;
        }

        sealed class ObservedModuleDataIO : ModuleDataIO
        {
            public OuterObserver Outer { get; } = new();
            public ObservedModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchService semanticSearch)
                : base(design, auth, db, files)
            {
                AddInterceptor(Outer);
                AddInterceptor(semanticSearch.Interceptor);
            }
        }

        [Test]
        public async Task 索引の書き込みの失敗は例外でなく結果のExceptionMessageで外側のインターセプタに返る()
        {
            //本体のインターセプタの約束 (失敗は結果の ExceptionMessage)。例外のまま投げると外側 (監査ログなど) には成功に見える
            await _db.ExecuteAsync(Ds, "CREATE TABLE notes (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, search_text TEXT, search_vector TEXT CHECK (search_vector IS NULL))", new());
            var note = new ModuleDesign { Name = "Note", DataSourceName = Ds, DbTable = "notes", CanCreate = true };
            note.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            note.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "題名", DbColumn = "title" });
            note.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            note.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(note);
            await _db.DisposeAsync();
            _db = NewDb();

            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Note" };
            data.Fields["Id"] = tempId;
            data.Fields["Title"] = new TextFieldData { Value = "新規" };
            var io = new ObservedModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), Indexer());
            var results = await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Note", Id = tempId.Value!, Add = [data] }]);

            Assert.That(results.Single().ExceptionMessage, Is.Not.Empty);
            Assert.That(io.Outer.Thrown, Is.Null, "例外のまま投げない");
            Assert.That(io.Outer.Results?.Single().ExceptionMessage, Is.Not.Empty, "外側には結果の ExceptionMessage で見える");
            var count = (await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM notes", new())).Single()["c"];
            Assert.That(Convert.ToInt32(count), Is.EqualTo(0), "保存は戻る");
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

        //意味検索のインターセプタより外側 (先に登録) で、next() の前に内部書き込みする (編集履歴の復活など)
        sealed class InnerWriter(Func<ModuleDataIO, Task> before) : IModuleDataIOInterceptor
        {
            public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
            {
                await before(io);
                return await next();
            }
            public Task GetListAsync(ModuleDataIO io, SearchCondition condition, Codeer.LowCode.Blazor.Utils.Paging<ModuleData> result) => Task.CompletedTask;
        }

        sealed class OuterFirstModuleDataIO : ModuleDataIO
        {
            public OuterFirstModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, IModuleDataIOInterceptor outer, SemanticSearchService semanticSearch)
                : base(design, auth, db, files)
            {
                AddInterceptor(outer);
                AddInterceptor(semanticSearch.Interceptor);
            }
        }

        ModuleDataIO CreateIOWithOuter(Func<ModuleDataIO, Task> before)
            => new OuterFirstModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), new InnerWriter(before), Indexer());

        [Test]
        public async Task 保存の中で内部追加した行にも索引が付く_編集履歴の復活相当()
        {
            string? addedId = null;
            var io = CreateIOWithOuter(async io =>
            {
                var row = new ModuleData { Name = "Inquiry" };
                row.Fields["Subject"] = new TextFieldData { Value = "復活した件名" };
                row.Fields["Body"] = new TextFieldData { Value = "復活した本文" };
                addedId = await io.CreateSystemAccess().AddAsync(row);
            });
            AssertNoError(await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry" }]));

            Assert.That(addedId, Is.EqualTo("4"), "採番された Id");
            var (text, vector) = await RowAsync(addedId!);
            Assert.That(text, Is.EqualTo("件名: 復活した件名\n本文: 復活した本文"));
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(text!))));
        }

        [Test]
        public async Task 保存の中で内部更新した行にも索引が付く()
        {
            var io = CreateIOWithOuter(async io =>
            {
                var row = new ModuleData { Name = "Inquiry" };
                row.Fields["Id"] = new IdFieldData { Value = "1" };
                row.Fields["Subject"] = new TextFieldData { Value = "内部で変えた件名" };
                await io.CreateSystemAccess().UpdateAsync(row);
            });
            AssertNoError(await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry" }]));

            var (text, vector) = await RowAsync("1");
            Assert.That(text, Is.EqualTo("件名: 内部で変えた件名\n本文: 注文がまだ届かない"));
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(text!))));
            Assert.That(await RowAsync("2"), Is.EqualTo(((string?)null, (string?)null)), "他の行は触らない");
        }

        [Test]
        public async Task 索引データだけを送った更新は捨てられ索引は変わらない()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));
            var before = await RowAsync("1");
            Assert.That(before.Text, Is.Not.Null);
            _embedding.Inputs.Clear();

            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Search"] = new SemanticSearchFieldData { Text = "t", Vector = "[1,2]" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "1", Update = [data] }]));

            Assert.That(await RowAsync("1"), Is.EqualTo(before), "送られた文章・ベクトルは書かれない");
            Assert.That(_embedding.Inputs, Is.Empty, "対象フィールドを書いていないので作り直さない");
        }

        [Test]
        public async Task 文章の列は全文_埋め込みには最大文字数まで渡す()
        {
            _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single().MaxTextLength = 10;
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));

            var (text, vector) = await RowAsync("1");
            const string full = "件名: 納期遅れの相談\n本文: 本文を変更";
            Assert.That(text, Is.EqualTo(full), "列には全文");
            Assert.That(_embedding.Inputs, Is.EqualTo(new[] { full[..10] }), "埋め込みには 10 文字まで");
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(full[..10]))));
        }

        sealed class CapturingLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
        {
            public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();
            public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(Entries);
            public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }
            public void Dispose() { }
            sealed class Logger(List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> entries) : Microsoft.Extensions.Logging.ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
                public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    lock (entries) entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }

        [Test]
        public async Task Interceptor未登録で一括取込のApplyAsyncを呼ぶと警告を1回だけ出す()
        {
            static ModuleData Row(string subject) { var d = new ModuleData { Name = "Inquiry" }; d.Fields["Subject"] = new TextFieldData { Value = subject }; return d; }
            static bool IsNotRegistered((Microsoft.Extensions.Logging.LogLevel Level, string Message) e) => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("Interceptor is not registered");

            var log = new CapturingLoggerFactory();
            var service = new SemanticSearchService(() => _embedding, () => _design, log);
            await service.ApplyAsync([Row("a")]);
            await service.ApplyAsync([Row("b")]);
#pragma warning disable CS0618 //旧結線の版とも共通 (合わせて 1 回)
            await service.ApplyAsync(Row("c"), isNewData: true);
#pragma warning restore CS0618
            Assert.That(log.Entries.Count(IsNotRegistered), Is.EqualTo(1));
            Assert.That(log.Entries.Single(IsNotRegistered).Message, Does.EndWith("otherwise saved rows are not indexed."));

            //Interceptor を登録したサービスでは出さない
            var registered = new CapturingLoggerFactory();
            var wired = new SemanticSearchService(() => _embedding, () => _design, registered);
            _ = CreateIO(wired);
            await wired.ApplyAsync([Row("d")]);
            Assert.That(registered.Entries.Count(IsNotRegistered), Is.EqualTo(0));
        }

        [Test]
        public async Task 削除日時方式のモジュールは索引済みのIdから削除済みの行を除く()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE docs (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, deleted_at TEXT, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO docs (title, deleted_at, search_text, search_vector) VALUES ('a', NULL, 'a', '[1]'), ('b', '2026-01-01 00:00:00', 'b', '[1]'), ('c', NULL, 'c', '[1]'), ('d', NULL, NULL, NULL)", new());
            var doc = new ModuleDesign { Name = "Doc", DataSourceName = Ds, DbTable = "docs" };
            doc.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            doc.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            doc.Fields.Add(new DateTimeFieldDesign { Name = SystemFieldNames.DeletedAt, DbColumn = "deleted_at" });
            var field = new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" };
            doc.Fields.Add(field);
            _design.AddModule(doc);
            var ids = await SemanticSearchIndexReader.ReadIndexedIdsAsync(_db, doc, field, CancellationToken.None);
            Assert.That(ids.OrderBy(e => e), Is.EqualTo(new[] { "1", "3" }), "削除日時に値のある行 (2) とベクトルの無い行 (4) は除く");
        }

        //トークンを見ずに返ってこないプロバイダ
        sealed class HangingEmbeddingProvider : Codeer.LowCode.Blazor.Extras.Server.AI.Embedding.IEmbeddingProvider
        {
            public string ModelId => "hanging";
            public int Dimensions => FakeEmbeddingProvider.Dimensions;
            public int Calls;
            public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref Calls);
                await Task.Delay(Timeout.Infinite);
                return [];
            }
        }

        [Test]
        public async Task トークンを見ないプロバイダでも保存時の上限時間で打ち切り文章だけ保存する()
        {
            var hanging = new HangingEmbeddingProvider();
            var service = new SemanticSearchService(() => hanging, () => _design) { SaveEmbeddingTimeout = TimeSpan.FromMilliseconds(200) };
            var submit = CreateIO(service).SubmitWithTransactionAsync([UpdateSubmit("1")]);
            Assert.That(await Task.WhenAny(submit, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(submit), "上限時間で待つのをやめる");
            AssertNoError(await submit);
            Assert.That(hanging.Calls, Is.EqualTo(1));
            Assert.That(await RowAsync("1"), Is.EqualTo(("件名: 納期遅れの相談\n本文: 本文を変更", (string?)null)), "文章だけ (ベクトルは NULL)");
        }

        [Test]
        public async Task 外側のインターセプタが削除を取り消した行の索引が作り直される()
        {
            await _db.ExecuteAsync(Ds, "UPDATE inquiries SET is_deleted = 1 WHERE id = 3", new());
            var io = CreateIOWithOuter(async io =>
            {
                var row = new ModuleData { Name = "Inquiry" };
                row.Fields["Id"] = new IdFieldData { Value = "3" };
                await io.UndeleteAsync("Inquiry", "3", row);
            });
            AssertNoError(await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry" }]));

            var (text, vector) = await RowAsync("3");
            Assert.That(text, Is.EqualTo("件名: 納品書の再発行\n本文: 納品書を紛失した"));
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(text!))));
            Assert.That(await RowAsync("1"), Is.EqualTo(((string?)null, (string?)null)), "他の行は触らない");
        }

        [Test]
        public async Task 自己参照の従属は直接の上司だけ作り直し祖父は対象外_ループしない()
        {
            //社員.部下 → 社員 (BossId = Id)。1 ← 2 ← 3 の上司関係で 3 を変えると、3 自身と直接の上司 2 だけ
            await _db.ExecuteAsync(Ds, "CREATE TABLE employees (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, boss_id INTEGER, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO employees (name, boss_id, search_text) VALUES ('社長', NULL, 'stale'), ('部長', 1, 'stale'), ('課長', 2, 'stale')", new());
            var employee = new ModuleDesign { Name = "Employee", DataSourceName = Ds, DbTable = "employees", CanCreate = true, CanUpdate = true, CanDelete = true };
            employee.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            employee.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "名前", DbColumn = "name" });
            employee.Fields.Add(new IdFieldDesign { Name = "BossId", DbColumn = "boss_id", IsManualInput = true });
            employee.Fields.Add(new ListFieldDesign
            {
                Name = "Subordinates",
                CanUpdate = true,
                SearchCondition = new SearchCondition
                {
                    ModuleName = "Employee",
                    Condition = MultiMatchCondition.And(new FieldVariableMatchCondition { SearchTargetVariable = "BossId.Value", Variable = "Id.Value", Comparison = MatchComparison.Equal }),
                },
            });
            employee.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", SourceFields = { "Name", "Subordinates" }, DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" });
            employee.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(employee);

            var data = new ModuleData { Name = "Employee" };
            data.Fields["Id"] = new IdFieldData { Value = "3" };
            data.Fields["Name"] = new TextFieldData { Value = "係長" };
            var submit = CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Employee", Id = "3", Update = [data] }]);
            Assert.That(await Task.WhenAny(submit, Task.Delay(TimeSpan.FromSeconds(30))), Is.SameAs(submit), "ループしない");
            AssertNoError(await submit);
            var texts = (await _db.QueryAsync(Ds, "SELECT search_text FROM employees ORDER BY id", new())).Select(r => r["search_text"]?.ToString()).ToArray();
            Assert.That(texts, Is.EqualTo(new[] { "stale", "名前: 部長", "名前: 係長" }), "自分 (3) と直接の上司 (2) だけ。祖父 (1) は対象外");
        }

        [Test]
        public async Task 最大文字数が0と負は切らない_全項目空の行は文章が空でベクトルNULL_埋め込みを呼ばない()
        {
            var field = _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single();
            const string full = "件名: 納期遅れの相談\n本文: 本文を変更";
            foreach (var max in new[] { 0, -1 })
            {
                field.MaxTextLength = max;
                _embedding.Inputs.Clear();
                AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1")]));
                Assert.That(_embedding.Inputs, Is.EqualTo(new[] { full }), $"MaxTextLength {max} は切らない");
                Assert.That((await RowAsync("1")).Vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(full))));
            }

            //全項目が空の行
            var calls = _embedding.Calls;
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = "2" };
            data.Fields["Subject"] = new TextFieldData { Value = null };
            data.Fields["Body"] = new TextFieldData { Value = null };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = "2", Update = [data] }]));
            Assert.That(await RowAsync("2"), Is.EqualTo(("", (string?)null)), "文章は空・ベクトルは NULL");
            Assert.That(_embedding.Calls, Is.EqualTo(calls), "埋め込みを呼ばない");
        }

        [Test]
        public async Task 条件で消す子の親の文章が作り直される()
        {
            //SearchDelete (条件で消す) の行の持ち主は保存の前に読んでおく
            await SetUpContactSearchAsync();
            var delete = new SearchCondition("ContactTags") { Condition = new FieldValueMatchCondition { SearchTargetVariable = "OwnerId.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("1") } };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Company", Id = "1", SearchDelete = [delete] }]));
            Assert.That((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS c FROM contact_tags WHERE owner_id = 1", new())).Single()["c"]?.ToString(), Is.EqualTo("0"));
            Assert.That(await ContactTextAsync("1"), Is.EqualTo("名前: A"), "タグを消した社員 1 の文章を作り直す");
            Assert.That(await ContactTextAsync("2"), Is.Null, "他の社員は触らない");
        }

        List<string> CheckCodes()
        {
            var field = _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single();
            return field.CheckDesign(new DesignCheckContext("Inquiry", _design, Utilities.CreateDataSource())).Select(e => e.Code).ToList();
        }
    }
}
