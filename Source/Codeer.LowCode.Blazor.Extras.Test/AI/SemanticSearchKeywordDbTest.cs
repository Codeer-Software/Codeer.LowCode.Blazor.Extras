using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// キーワード検索 (SemanticMatchCondition.Keywords): 文章の列への LIKE の AND を、本体の一覧検索 (権限・論理削除つき) で実 DB (SQLite) に通す。
    /// 埋め込みは使わないので、ベクトル検索に対応しない DB でも動くことの確認でもある。
    /// </summary>
    public class SemanticSearchKeywordDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "is_deleted" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector", KeywordSearch = true });
            m.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(m);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"semantic_keyword_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, is_deleted INTEGER DEFAULT 0, search_text TEXT, search_vector TEXT)", new());
            //文章の列は保存時にクライアントが「表示名: 値」で作るもの。ここでは出来上がった文章を直接入れる
            await _db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject, is_deleted, search_text) VALUES " +
                "('納期遅れの相談', 0, '件名: 納期遅れの相談\n本文: 注文がまだ届かない\n得意先: 青空商事'), " +
                "('請求書の再発行', 0, '件名: 請求書の再発行\n本文: 宛名を変更して再発行してほしい\n得意先: 緑山工業'), " +
                "('納品書の再発行', 0, '件名: 納品書の再発行\n本文: 納品書を紛失した。納期は守られた\n得意先: 青空商事'), " +
                "('削除済みの納期相談', 1, '件名: 削除済みの納期相談\n本文: 納期\n得意先: 青空商事'), " +
                "('割引の問い合わせ', 0, '件名: 割引の問い合わせ\n本文: 100%_OFF の条件\n得意先: 緑山工業'), " +
                "('索引なし', 0, NULL)", new());
            _design = CreateDesign();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        async Task<List<string>> SearchAsync(string keywords, MatchConditionBase? other = null)
        {
            var keyword = new SemanticMatchCondition { FieldName = "Search", Keywords = keywords };
            var condition = new SearchCondition("Inquiry")
            {
                Condition = other == null ? keyword : MultiMatchCondition.And(other, keyword),
                SelectFields = ["Id", "Subject"],
            };
            condition.SortConditions.Add(new SortCondition { Variable = "Id.Value" });
            var page = await CreateIO().GetListAsync(condition, 0);
            return page.Items.Select(e => ((TextFieldData)e.Fields["Subject"]).Value!).ToList();
        }

        [Test]
        public async Task すべての語を含む行に絞る_語の順は問わない_全角スペースも区切り()
        {
            Assert.That(await SearchAsync("納期"), Is.EqualTo(new[] { "納期遅れの相談", "納品書の再発行" }), "論理削除の行は出ない・索引の無い行は出ない");
            Assert.That(await SearchAsync("納期 青空"), Is.EqualTo(new[] { "納期遅れの相談", "納品書の再発行" }));
            Assert.That(await SearchAsync("青空　納期 紛失"), Is.EqualTo(new[] { "納品書の再発行" }));
            Assert.That(await SearchAsync("再発行 緑山"), Is.EqualTo(new[] { "請求書の再発行" }), "項目をまたいで AND");
            Assert.That(await SearchAsync("納期 請求書"), Is.Empty, "同じ行に両方なければ当たらない");
        }

        [Test]
        public async Task ワイルドカードは文字として扱う()
        {
            Assert.That(await SearchAsync("100%_OFF"), Is.EqualTo(new[] { "割引の問い合わせ" }));
            Assert.That(await SearchAsync("%"), Is.EqualTo(new[] { "割引の問い合わせ" }), "% は文字 (全行には当たらない)");
            Assert.That(await SearchAsync("納_"), Is.Empty, "_ は任意の 1 文字ではない");
        }

        [Test]
        public async Task 普通の条件と組み合わせられる()
        {
            var subject = new FieldValueMatchCondition { SearchTargetVariable = "Subject.Value", Comparison = MatchComparison.Like, Value = MultiTypeValue.Create("再発行") };
            Assert.That(await SearchAsync("青空", subject), Is.EqualTo(new[] { "納品書の再発行" }), "本体の Like (escape パラメータ) と同居しても動く");
        }

        [Test]
        public async Task キーワードだけのとき一覧の並びに近い順があっても飛ばして普段の並びになる()
        {
            //一覧の既定の並びに「Search.Value (近い順) → Subject 降順」を入れておく構成 (docs の推奨)。キーワードだけなら近い順は作れないので本体が飛ばす (core 1.3.49)
            var condition = new SearchCondition("Inquiry") { Condition = new SemanticMatchCondition { FieldName = "Search", Keywords = "納期" }, SelectFields = ["Id", "Subject"] };
            condition.SortConditions.Add(new SortCondition { Variable = "Search.Value" });
            condition.SortConditions.Add(new SortCondition { Variable = "Subject.Value", IsDescending = true });
            var page = await CreateIO().GetListAsync(condition, 0);
            Assert.That(page.Items.Select(e => ((TextFieldData)e.Fields["Subject"]).Value), Is.EqualTo(new[] { "納期遅れの相談", "納品書の再発行" }));
        }

        [Test]
        public void 文章もキーワードも無い条件_文章だけでベクトルの無い条件は例外()
        {
            Assert.That(async () => await SearchAsync("  "), Throws.TypeOf<LowCodeException>().With.Message.Contains("Search"));
            var textOnly = new SearchCondition("Inquiry") { Condition = new SemanticMatchCondition { FieldName = "Search", Text = "納期の遅れ" } };
            Assert.That(async () => await CreateIO().GetListAsync(textOnly, 0), Throws.TypeOf<LowCodeException>(), "前処理 (埋め込み) が結線されていない");
        }
    }
}
