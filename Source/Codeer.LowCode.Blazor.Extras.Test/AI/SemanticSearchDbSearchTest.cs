using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// DB 側のベクトル検索 (pgvector / SQL Server 2025): 距離順 SELECT の組み立て、ベクトルリテラル、
    /// execute_sql の {embed:…} 置換。実 DB には接続しない (文字列の検証)。
    /// </summary>
    public class SemanticSearchDbSearchTest
    {
        static DesignData CreateDesign(string dataSource, string vectorSearchColumn)
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = dataSource, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = vectorSearchColumn });
            d.AddModule(m);
            return d;
        }

        //接続はしない (DataSource の定義だけ使う) ので接続文字列はダミー
        static DbAccessor Db(DataSourceType type) => new([new DataSource { Name = "Main", DataSourceType = type, ConnectionString = "Host=none" }]);

        [Test]
        public void PostgreSQLの距離順SELECT()
        {
            var sql = SemanticSearchIndexReader.BuildSearchSql(DataSourceType.PostgreSQL, "inquiries", "id", "search_text", "search_vector_v", "is_deleted", "'[1,2]'::vector", 5);
            Assert.That(sql, Does.StartWith("select \"id\", \"search_text\", 1 - (\"search_vector_v\" <=> '[1,2]'::vector) as semantic_score from \"inquiries\""));
            Assert.That(sql, Does.Contain("where \"search_vector_v\" is not null and (\"is_deleted\" is null or cast(\"is_deleted\" as integer) = 0)"));
            Assert.That(sql, Does.EndWith("order by \"search_vector_v\" <=> '[1,2]'::vector limit 5"));

            var noDelete = SemanticSearchIndexReader.BuildSearchSql(DataSourceType.PostgreSQL, "t", "id", "txt", "vec", null, "'[1]'::vector", 3);
            Assert.That(noDelete, Does.Contain("where \"vec\" is not null order by"));
        }

        [Test]
        public void SQLServerの距離順SELECT()
        {
            var sql = SemanticSearchIndexReader.BuildSearchSql(DataSourceType.SQLServer, "inquiries", "id", "search_text", "search_vector", "is_deleted", "CAST('[1,2]' AS VECTOR(2))", 5);
            Assert.That(sql, Does.StartWith("select top (5) [id], [search_text], 1 - VECTOR_DISTANCE('cosine', [search_vector], CAST('[1,2]' AS VECTOR(2))) as semantic_score from [inquiries]"));
            Assert.That(sql, Does.EndWith("order by VECTOR_DISTANCE('cosine', [search_vector], CAST('[1,2]' AS VECTOR(2)))"));
        }

        [Test]
        public void 対応しないDBは例外()
        {
            Assert.That(() => SemanticSearchIndexReader.BuildSearchSql(DataSourceType.SQLite, "t", "id", "txt", "vec", null, "x", 1), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => SemanticSearchIndexReader.VectorLiteral(DataSourceType.MySQL, [1f]), Throws.TypeOf<NotSupportedException>());
            Assert.That(SemanticSearchIndexReader.SupportsDbSearch(DataSourceType.PostgreSQL), Is.True);
            Assert.That(SemanticSearchIndexReader.SupportsDbSearch(DataSourceType.SQLServer), Is.True);
            Assert.That(SemanticSearchIndexReader.SupportsDbSearch(DataSourceType.SQLite), Is.False);
            Assert.That(SemanticSearchIndexReader.SupportsDbSearch(DataSourceType.Oracle), Is.False);
        }

        [Test]
        public void ベクトルリテラルは方言ごと()
        {
            Assert.That(SemanticSearchIndexReader.VectorLiteral(DataSourceType.PostgreSQL, [1f, -0.5f]), Is.EqualTo("'[1,-0.5]'::vector"));
            Assert.That(SemanticSearchIndexReader.VectorLiteral(DataSourceType.SQLServer, [1f, -0.5f, 0.25f]), Is.EqualTo("CAST('[1,-0.5,0.25]' AS VECTOR(3))"));
        }

        [Test]
        public async Task execute_sqlのembedプレースホルダーを埋め込みリテラルに置き換える()
        {
            var embedding = new FakeEmbeddingProvider();
            var design = CreateDesign("Main", "search_vector_v");
            var toolSet = new SemanticSearchToolSet(() => design, () => Db(DataSourceType.PostgreSQL), () => embedding, ["Main"]);

            const string plain = "select count(*) from inquiries";
            Assert.That(await toolSet.ExpandEmbeddingsAsync("Main", plain, CancellationToken.None), Is.EqualTo(plain), "無ければそのまま");

            var sql = "select id, subject from inquiries where customer_id = 3 order by search_vector_v <=> {embed:納期遅れのクレーム} limit 5";
            var expanded = await toolSet.ExpandEmbeddingsAsync("Main", sql, CancellationToken.None);
            var expected = SemanticSearchIndexReader.VectorLiteral(DataSourceType.PostgreSQL, FakeEmbeddingProvider.Embed("納期遅れのクレーム"));
            Assert.That(expanded, Is.EqualTo($"select id, subject from inquiries where customer_id = 3 order by search_vector_v <=> {expected} limit 5"));
            Assert.That(expanded, Does.Not.Contain("{embed"));

            //複数はそれぞれの内容で置き換わる
            var two = await toolSet.ExpandEmbeddingsAsync("Main", "select 1 - (v <=> {embed:A}) as a, 1 - (v <=> {embed: B }) as b from t", CancellationToken.None);
            Assert.That(two, Does.Contain(SemanticSearchIndexReader.VectorLiteral(DataSourceType.PostgreSQL, FakeEmbeddingProvider.Embed("A"))));
            Assert.That(two, Does.Contain(SemanticSearchIndexReader.VectorLiteral(DataSourceType.PostgreSQL, FakeEmbeddingProvider.Embed("B"))));

            //空の内容は拒否
            Assert.That(async () => await toolSet.ExpandEmbeddingsAsync("Main", "select {embed: } from t", CancellationToken.None), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task 対応しないデータソースではembedを拒否する()
        {
            var embedding = new FakeEmbeddingProvider();
            var design = CreateDesign("Main", "search_vector_v");
            var toolSet = new SemanticSearchToolSet(() => design, () => Db(DataSourceType.SQLite), () => embedding, ["Main"]);
            Assert.That(async () => await toolSet.ExpandEmbeddingsAsync("Main", "select * from t order by v <=> {embed:x}", CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contain("対応していない"));
        }
    }
}
