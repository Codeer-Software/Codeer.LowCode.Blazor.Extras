using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// SemanticSearchService (保存時の索引付け): 実 DB (SQLite) で、保存時に文章とベクトルが書き込み専用列に入ること、
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

        /// <summary>テンプレートの CustomizedModuleDataIO と同じ結線 (Add / Update の前に ApplyAsync)。</summary>
        sealed class IndexingModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchService semanticSearch)
            : ModuleDataIO(design, auth, db, files)
        {
            protected override async Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
            {
                await semanticSearch.ApplyAsync(data, isNewData: true);
                return await base.AddAsync(transactionId, moduleSubmitId, data);
            }

            protected override async Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
            {
                await semanticSearch.ApplyAsync(data, isNewData: false);
                await base.UpdateAsync(transactionId, moduleSubmitId, data);
            }
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
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, body TEXT, is_deleted INTEGER DEFAULT 0, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject, body) VALUES ('納期遅れの相談', '注文がまだ届かない'), ('請求書の再発行', '宛名を変更して再発行してほしい'), ('納品書の再発行', '納品書を紛失した')", new());
            _design = CreateDesign();
            _embedding = new FakeEmbeddingProvider();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        SemanticSearchService Indexer(bool withEmbedding = true) => new(withEmbedding ? () => _embedding : () => null, () => _design);
        ModuleDataIO CreateIO(SemanticSearchService indexer) => new IndexingModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), indexer);

        async Task<(string? Text, string? Vector)> RowAsync(string id)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT search_text, search_vector FROM inquiries WHERE id = {id}", new())).Single();
            var values = row.Values.ToList();
            return (values[0] is null or DBNull ? null : values[0].ToString(), values[1] is null or DBNull ? null : values[1].ToString());
        }

        static ModuleSubmitData UpdateSubmit(string id, string? text)
        {
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Body"] = new TextFieldData { Value = "本文を変更" };
            if (text != null) data.Fields["Search"] = new SemanticSearchFieldData { Text = text };
            return new ModuleSubmitData { ModuleName = "Inquiry", Id = id, Update = [data] };
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        [Test]
        public async Task 更新で送られた文章にベクトルを付けて書き込み専用列へ保存する()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1", "件名: 納期遅れの相談\n本文: 本文を変更")]));
            var (text, vector) = await RowAsync("1");
            Assert.That(text, Is.EqualTo("件名: 納期遅れの相談\n本文: 本文を変更"));
            Assert.That(vector, Is.EqualTo(SemanticSearchVector.Encode(FakeEmbeddingProvider.Embed(text!))));
            Assert.That(_embedding.Inputs, Is.EqualTo(new[] { text }));
            //他の行は触らない
            Assert.That(await RowAsync("2"), Is.EqualTo(((string?)null, (string?)null)));
        }

        [Test]
        public async Task 文章が送られていない更新では索引を触らない()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1", null)]));
            Assert.That(await RowAsync("1"), Is.EqualTo(((string?)null, (string?)null)));
            Assert.That(_embedding.Inputs, Is.Empty);
        }

        [Test]
        public async Task 新規行は文章が無ければサーバーで組み立てる()
        {
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = tempId;
            data.Fields["Subject"] = new TextFieldData { Value = "一括取込の件名" };
            data.Fields["Body"] = new TextFieldData { Value = "取込の本文" };
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Inquiry", Id = tempId.Value!, Add = [data] }]));
            var (text, vector) = await RowAsync("4");
            Assert.That(text, Is.EqualTo("件名: 一括取込の件名\n本文: 取込の本文"));
            Assert.That(vector, Is.Not.Null);
        }

        [Test]
        public async Task 埋め込みモデルが無い又は失敗なら文章だけ保存する()
        {
            AssertNoError(await CreateIO(Indexer(withEmbedding: false)).SubmitWithTransactionAsync([UpdateSubmit("1", "件名: A")]));
            Assert.That(await RowAsync("1"), Is.EqualTo(("件名: A", (string?)null)));

            _embedding.Fail = true;
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("2", "件名: B")]));
            Assert.That(await RowAsync("2"), Is.EqualTo(("件名: B", (string?)null)));
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
        }

        [Test]
        public async Task 書き込み専用列は読み込みで来ない()
        {
            AssertNoError(await CreateIO(Indexer()).SubmitWithTransactionAsync([UpdateSubmit("1", "件名: A")]));
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

            field.DbColumnVector = "";
            Assert.That(CheckCodes(), Does.Contain(DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), 1 /* ColumnsRequired */)));
            field.DbColumnVector = "search_vector";

            field.DbColumnVectorSearch = "";
            Assert.That(CheckCodes(), Does.Contain(DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), 1)), "ベクトル検索用の列も必須");
            field.DbColumnVectorSearch = "search_vector";
            field.SourceFields.Add("NoSuchField");
            var codes = CheckCodes();
            Assert.That(codes, Does.Not.Contain(DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), 1)));
            Assert.That(codes.Count, Is.EqualTo(1), string.Join(", ", codes));
        }

        [Test]
        public void デザインチェックは詳細レイアウトが文章のフィールドを読み込まないことを指摘する()
        {
            var module = _design.Modules.Find("Inquiry")!;
            var field = module.Fields.OfType<SemanticSearchFieldDesign>().Single();
            var code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), 2 /* FieldsNotLoaded */);
            static GridLayoutDesign Grid(params string[] fieldNames)
            {
                var grid = new GridLayoutDesign();
                var row = new GridRow();
                foreach (var name in fieldNames) row.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(name) });
                grid.Rows.Add(row);
                return grid;
            }

            //Subject だけのレイアウト = Body が読み込まれない
            module.DetailLayouts["Edit"] = new DetailLayoutDesign { Layout = Grid("Subject") };
            var infos = field.CheckDesign(new DesignCheckContext("Inquiry", _design, Utilities.CreateDataSource()));
            var info = infos.OfType<LayoutDesignCheckInfo>().Single(e => e.Code == code);
            Assert.That(info.Location.Layout, Is.EqualTo("Edit"));
            Assert.That(info.Message, Does.Contain("Body").And.Not.Contain("Subject"));

            //Body を DataOnlyFields に足せば読み込まれる
            module.DetailLayouts["Edit"].DataOnlyFields.Add("Body");
            Assert.That(CheckCodes(), Does.Not.Contain(code));

            //SourceFields を設定してこのフィールドを DataOnlyFields に置けば、依存先 (IDataDependentField) として読み込まれる
            module.DetailLayouts["Edit"].DataOnlyFields.Clear();
            module.DetailLayouts["Edit"].DataOnlyFields.Add("Search");
            Assert.That(CheckCodes(), Does.Contain(code), "SourceFields が空なら依存先を列挙できないので指摘される");
            field.SourceFields.AddRange(["Subject", "Body"]);
            Assert.That(CheckCodes(), Does.Not.Contain(code));
            Assert.That(field.GetDependencyFields(), Is.EqualTo(new[] { "Subject", "Body" }));

            //全部レイアウトに置いてあれば指摘なし
            field.SourceFields.Clear();
            module.DetailLayouts["Edit"] = new DetailLayoutDesign { Layout = Grid("Subject", "Body") };
            Assert.That(CheckCodes(), Is.Empty);

            //対象フィールドを 1 つも読み込まないレイアウト (既定の空レイアウト等) は対象外
            module.DetailLayouts["Edit"] = new DetailLayoutDesign { Layout = Grid() };
            Assert.That(CheckCodes(), Is.Empty);
            module.DetailLayouts.Remove("Edit");
        }

        List<string> CheckCodes()
        {
            var field = _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single();
            return field.CheckDesign(new DesignCheckContext("Inquiry", _design, Utilities.CreateDataSource())).Select(e => e.Code).ToList();
        }
    }
}
