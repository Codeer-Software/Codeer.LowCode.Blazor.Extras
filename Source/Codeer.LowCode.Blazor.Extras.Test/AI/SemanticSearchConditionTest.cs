using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// 画面の検索欄からの意味検索: SemanticSearchField が作る条件 (文章だけ) と、サーバーの前処理 (SemanticSearchService.ConditionInterceptor) が
    /// 文章を埋め込みにすること。検索の中身 (SQL・権限) は本体と SemanticSearchFieldDesign のまま (距離の計算は SemanticSearchVectorDbTest の実 DB)。
    /// </summary>
    public class SemanticSearchConditionTest : IAuthenticationContext
    {
        const string Ds = "Main";

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        static DesignData CreateDesign(double? maxDistance = null)
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Note", DataSourceName = Ds, DbTable = "notes" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector", SearchMaxDistance = maxDistance });
            d.AddModule(m);
            return d;
        }

        [Test]
        public async Task 検索欄の文章は文章だけの意味検索の条件になり戻せて消せる()
        {
            var design = CreateDesign(maxDistance: 0.5);
            var module = await ModuleCreationService.CreateModuleAsync(new TestServices(design).Core, new ModuleData { Name = "Note" });
            var field = (SemanticSearchField)module.GetField("Search")!;

            Assert.That(field.GetMatchCondition(), Is.Null, "空なら条件なし");
            await field.SetSearchTextAsync("  納期の遅れ  ");
            var condition = field.GetMatchCondition() as FieldMatchCondition;
            Assert.That(condition!.FieldName, Is.EqualTo("Search"));
            var semantic = (SemanticMatchCondition)condition.Children.Single();
            Assert.That(semantic.FieldName, Is.EqualTo("Search"));
            Assert.That(semantic.Text, Is.EqualTo("納期の遅れ"));
            Assert.That(semantic.Vector, Is.Empty, "埋め込みはサーバーが作る");
            Assert.That(semantic.MaxDistance, Is.EqualTo(0.5), "距離の上限は設計から");

            //URL 等から戻す (JSON を往復しても型が保たれる)
            await field.ClearMatchConditionAsync();
            Assert.That(field.SearchText, Is.Null);
            await field.SetMatchConditionAsync(condition.JsonClone());
            Assert.That(field.SearchText, Is.EqualTo("納期の遅れ"));
        }

        [Test]
        public async Task 前処理は文章だけの条件に埋め込みを入れベクトル済みは触らない()
        {
            var embedding = new FakeEmbeddingProvider();
            var interceptor = new SemanticSearchService(() => embedding, () => CreateDesign()).ConditionInterceptor;

            var textOnly = new SemanticMatchCondition { FieldName = "Search", Text = "納期の遅れ" };
            var already = new SemanticMatchCondition { FieldName = "Search", Text = "x", Vector = [1f, 2f] };
            var sameText = new SemanticMatchCondition { FieldName = "Search", Text = "納期の遅れ " };
            var condition = new SearchCondition("Note")
            {
                Condition = MultiMatchCondition.And(new FieldMatchCondition { FieldName = "Search", Children = [textOnly] }, MultiMatchCondition.Or(already, sameText)),
            };
            await interceptor.PrepareConditionAsync(null!, condition);

            Assert.That(textOnly.Vector, Is.EqualTo(FakeEmbeddingProvider.Embed("納期の遅れ")));
            Assert.That(sameText.Vector, Is.EqualTo(textOnly.Vector));
            Assert.That(already.Vector, Is.EqualTo(new[] { 1f, 2f }));
            Assert.That(embedding.Calls, Is.EqualTo(1), "同じ文章はまとめて 1 回");

            //意味検索の無い条件では埋め込みを呼ばない
            await interceptor.PrepareConditionAsync(null!, new SearchCondition("Note"));
            Assert.That(embedding.Calls, Is.EqualTo(1));
        }

        [Test]
        public void 埋め込みプロバイダが無ければ理由つきで失敗する()
        {
            var interceptor = new SemanticSearchService(() => null, () => CreateDesign()).ConditionInterceptor;
            var condition = new SearchCondition("Note") { Condition = new SemanticMatchCondition { FieldName = "Search", Text = "x" } };
            Assert.That(async () => await interceptor.PrepareConditionAsync(null!, condition), Throws.TypeOf<LowCodeException>());
        }

        [Test]
        public void 検索欄の文章が一覧に効かない構成は設計チェックが指摘する()
        {
            var design = CreateDesign();
            var module = design.Modules.Find("Note")!;
            var field = module.Fields.OfType<SemanticSearchFieldDesign>().Single();
            var code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), 3 /* SearchHasNoEffect */);
            List<DesignCheckInfo> Check() => field.CheckDesign(new DesignCheckContext("Note", design, Utilities.CreateDataSource())).Where(e => e.Code == code).ToList();
            static SearchGridLayoutDesign Grid(params string[] fieldNames)
            {
                var grid = new SearchGridLayoutDesign();
                var row = new GridRow();
                foreach (var name in fieldNames) row.Columns.Add(new GridColumn { Layout = new FieldLayoutDesign(name) });
                grid.Rows.Add(row);
                return grid;
            }

            //ページの一覧 (既定の検索レイアウト)。検索レイアウトに置いていなければ対象外
            var list = new ListFieldDesign();
            list.SearchCondition.ModuleName = "Note";
            var frame = new PageFrameDesign { Name = "Main", IsApplicationRoot = true };
            frame.Left.Links.Add(new PageLink { Title = "Note", Module = "Note", ModulePageType = ModulePageType.Auto, ListPageDesign = new ListPageDesign { ListFieldDesign = list } });
            ((IEditablePageFrameDesign)design.PageFrames).Add(frame);
            module.SearchLayouts[""] = new SearchLayoutDesign { Layout = Grid("Title") };
            Assert.That(Check(), Is.Empty);

            //検索レイアウトに置いたが、並びにも距離の上限にも無い = 文章を入れても一覧が変わらない
            module.SearchLayouts[""] = new SearchLayoutDesign { Layout = Grid("Title", "Search") };
            var info = Check().Single();
            Assert.That(info.Message, Does.Contain("PageFrame Main").And.Contain("Search.Value"));

            //並びに入れれば効く
            list.SearchCondition.SortConditions.Add(new SortCondition { Variable = "Search.Value" });
            Assert.That(Check(), Is.Empty);

            //距離の上限があれば並びに無くても絞られる
            list.SearchCondition.SortConditions.Clear();
            field.SearchMaxDistance = 0.5;
            Assert.That(Check(), Is.Empty);
            field.SearchMaxDistance = null;

            //SearchField の結果の一覧も対象 (別の検索レイアウトを使う)
            module.SearchLayouts[""] = new SearchLayoutDesign { Layout = Grid("Title") };
            module.SearchLayouts["Semantic"] = new SearchLayoutDesign { Layout = Grid("Search") };
            var owner = new ModuleDesign { Name = "Portal" };
            var results = new ListFieldDesign { Name = "Notes" };
            results.SearchCondition.ModuleName = "Note";
            owner.Fields.Add(results);
            owner.Fields.Add(new SearchFieldDesign { Name = "NoteSearch", ResultsViewFieldName = "Notes", LayoutName = "Semantic" });
            design.AddModule(owner);
            Assert.That(Check().Single().Message, Does.Contain("Portal.NoteSearch"));
            results.SearchCondition.SortConditions.Add(new SortCondition { Variable = "Search.Value" });
            Assert.That(Check(), Is.Empty);
        }

        [Test]
        public async Task 一覧検索は前処理で埋め込んでから意味検索のSQLまで進む()
        {
            var file = Path.Combine(Path.GetTempPath(), $"semantic_condition_{Guid.NewGuid():N}.db");
            var sources = new[] { new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={file}" } };
            try
            {
                await using var db = new DbAccessor(sources);
                await db.ExecuteAsync(Ds, "CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT, search_text TEXT, search_vector TEXT)", new());
                var design = CreateDesign();
                var condition = new SearchCondition("Note") { Condition = new SemanticMatchCondition { FieldName = "Search", Text = "納期の遅れ" } };
                condition.SortConditions.Add(new SortCondition { Variable = "Search.Value" });

                //前処理なし: ベクトルが無いので、結線を促す理由で止まる
                var plain = new ModuleDataIO(design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>()));
                Assert.That(async () => await plain.GetListAsync(condition.JsonClone(), 0), Throws.TypeOf<LowCodeException>().With.Message.Contains("AddInterceptor"));

                //前処理あり: ベクトルが入り、SQL の組み立てまで進む (SQLite はベクトル検索できないのでその理由で止まる)
                var io = new ModuleDataIO(design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>()));
                io.AddInterceptor(new SemanticSearchService(() => new FakeEmbeddingProvider(), () => design).ConditionInterceptor);
                Assert.That(async () => await io.GetListAsync(condition.JsonClone(), 0), Throws.TypeOf<LowCodeException>().With.Message.Contains("SQLite"));

                //検索欄が空 (条件なし) なら、一覧の並びに意味検索が入っていても飛ばして普通に読める
                var empty = new SearchCondition("Note");
                empty.SortConditions.Add(new SortCondition { Variable = "Search.Value" });
                empty.SortConditions.Add(new SortCondition { Variable = "Title.Value" });
                Assert.That((await io.GetListAsync(empty, 0)).Items, Is.Empty);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(file)) File.Delete(file);
            }
        }
    }
}
