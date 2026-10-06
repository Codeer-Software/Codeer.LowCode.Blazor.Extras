using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Utils;
using Excel.Report.PDF;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>
    /// TagField を実 DB (SQLite) で: タグ付け行の読み込み・保存 (本体の保存に載る。新しいタグはマスタの行も同じトランザクション)、
    /// 検索 (いずれか = 子のパス、すべて = 全部持つレコードの Id)、一覧の行のまとめ読み、意味検索の文章、一括ダウンロード。
    /// データ: A = 展示会, DXPO / B = 展示会 / C = セミナー / D = なし。
    /// </summary>
    public class TagFieldDbTest : IAuthenticationContext
    {
        const string Ds = TagTestDesigns.Ds;
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"tag_field_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile};Foreign Keys=True" }]);
            await TagTestDesigns.CreateTablesAsync(_db);
            _design = TagTestDesigns.Create();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        DbClientServices Client() => new(_design, CreateIO);

        static TagField Tags(Module module) => (TagField)module.GetField("Tags")!;

        async Task<string> LinksAsync()
        {
            var rows = await _db.QueryAsync(Ds, "SELECT c.name AS c, t.name AS t FROM contact_tags l JOIN contacts c ON c.id = l.contact_id JOIN tags t ON t.id = l.tag_id ORDER BY c.name, l.id", new());
            return string.Join(" ", rows.Select(r => $"{r["c"]}:{r["t"]}"));
        }

        async Task<string> MasterAsync()
        {
            var rows = await _db.QueryAsync(Ds, "SELECT name FROM tags ORDER BY id", new());
            return string.Join(",", rows.Select(r => r["name"]));
        }

        #region 読み込み・保存

        [Test]
        public async Task 詳細でタグ付け行を付けた順に読む()
        {
            var client = Client();
            Assert.That(Tags(await client.OpenAsync("Contact", "1")).Tags, Is.EqualTo(new[] { "展示会", "DXPO" }));
            Assert.That(Tags(await client.OpenAsync("Contact", "4")).Tags, Is.Empty);
            Assert.That(client.Logger.ErrorList, Is.Empty);
        }

        [Test]
        public async Task 足し外しは本体の保存でタグ付け行になる()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "2");
            var field = Tags(module);
            await field.AddTagAsync("セミナー");
            await field.RemoveTagAsync("展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "セミナー" }));
            Assert.That(field.IsModified, Is.True);
            Assert.That(await LinksAsync(), Does.Contain("B:展示会"), "保存までは DB は変わらない");

            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Is.EqualTo("A:展示会 A:DXPO B:セミナー C:セミナー"));
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー,100%達成"), "マスタは増えない");
        }

        [Test]
        public async Task 大文字小文字違いはマスタの表記に寄せ重ねない()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "2");
            var field = Tags(module);
            await field.AddTagAsync("dxpo");
            await field.AddTagAsync("展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "DXPO" }));
            Assert.That(field.HasTag("Dxpo"), Is.True);
            Assert.That(await module.SubmitAsync(), Is.True);
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー,100%達成"));
        }

        [Test]
        public async Task 新しいタグはマスタの行も同じ保存で作る()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            var field = Tags(module);
            await field.AddTagAsync("新規A, 新規B");
            await field.RemoveTagAsync("新規B");
            Assert.That(field.Tags, Is.EqualTo(new[] { "新規A" }));

            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(client.SubmitCalls, Has.Count.EqualTo(1), "1 回の保存 (1 トランザクション)");
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー,100%達成,新規A"), "外した新しいタグは作らない");
            Assert.That(await LinksAsync(), Does.Contain("D:新規A"));

            //保存後: チップは実 Id のタグを指し、候補にも入る
            var reopened = Tags(await client.OpenAsync("Contact", "4"));
            Assert.That(reopened.Tags, Is.EqualTo(new[] { "新規A" }));
            Assert.That(await Tags(module).GetCandidatesAsync(), Does.Contain("新規A"));
        }

        [Test]
        public async Task AllowNewTagsがfalseならマスタに無いタグは入らない()
        {
            _design = TagTestDesigns.Create(e => e.AllowNewTags = false);
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            var field = Tags(module);
            await field.AddTagAsync("新規");
            Assert.That(field.Tags, Is.Empty);
            Assert.That(field.IsValid, Is.False);
            Assert.That(field.ErrorText, Does.Contain("新規"));

            await field.AddTagAsync("展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会" }));
            Assert.That(field.IsValid, Is.True, "足せたらエラーは消える");
        }

        [Test]
        public async Task 候補の行数を超えるタグも入力すればマスタから引き重複を作らない()
        {
            _design = TagTestDesigns.Create(e => e.CandidateRowCount = 1);
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            var field = Tags(module);
            Assert.That(await field.GetCandidatesAsync(), Has.Count.EqualTo(1));
            await field.AddTagAsync("セミナー");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー,100%達成"));
            Assert.That(await LinksAsync(), Does.Contain("D:セミナー"));
        }

        [Test]
        public async Task 新規レコードのタグはそのレコードのIdで保存する()
        {
            var client = Client();
            var module = await client.CreateNewAsync("Contact");
            await module.GetField<TextField>("Name")!.SetValueAsync("E");
            await Tags(module).AddTagAsync("DXPO, 新規");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.Contain("E:DXPO E:新規"));
        }

        [Test]
        public async Task レコードを消せばタグ付け行も消えマスタは残る()
        {
            var io = CreateIO();
            var results = await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Contact", Id = "1", Delete = { new() { Id = "1", ModuleName = "Contact" } } }]);
            Assert.That(results.Select(e => e.ExceptionMessage).Where(e => !string.IsNullOrEmpty(e)), Is.Empty);
            Assert.That(await LinksAsync(), Is.EqualTo("B:展示会 C:セミナー"));
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー,100%達成"));
        }

        [Test]
        public async Task 使われているタグはマスタから消せず使われていなければ消せる()
        {
            //このテストの DB は外部キーを強制している (Foreign Keys=True)。上の削除・保存のテストはその下で通っている
            var io = CreateIO();
            var inUse = await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Tag", Id = "1", Delete = { new() { Id = "1", ModuleName = "Tag" } } }]);
            Assert.That(inUse.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, "展示会は A と B に付いている");
            var unused = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Tag", Id = "4", Delete = { new() { Id = "4", ModuleName = "Tag" } } }]);
            Assert.That(unused.Select(e => e.ExceptionMessage).Where(e => !string.IsNullOrEmpty(e)), Is.Empty);
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー"));
        }

        [Test]
        public async Task マスタの照合が大文字小文字を区別するDBでも既存のタグを引く()
        {
            //マスタの name を大文字小文字を区別する列に作り直す (PostgreSQL・既定の SQLite と同じ)。候補は 1 行だけ読む
            await _db.ExecuteAsync(Ds, "PRAGMA foreign_keys = OFF", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE tags2 (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO tags2 (id, name) SELECT id, name FROM tags", new());
            await _db.ExecuteAsync(Ds, "DROP TABLE tags", new());
            await _db.ExecuteAsync(Ds, "ALTER TABLE tags2 RENAME TO tags", new());
            DbAccessor.ClearTableDefinitionCache();
            _design = TagTestDesigns.Create(e => e.CandidateRowCount = 1);

            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            await Tags(module).AddTagAsync("dxpo");
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "DXPO" }), "部分一致で引いて手元で比べる");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await MasterAsync(), Is.EqualTo("展示会,DXPO,セミナー,100%達成"), "重複を作らない");
        }

        [Test]
        public async Task スクリプトで読んだレコードでも付いているタグを重ねず足せる()
        {
            //ModuleSearcher で読んだレコードと同じ: レイアウトなしで作る (本体は子の一覧を読まない)
            var client = Client();
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact", Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = "Id.Value", Comparison = MatchComparison.Equal, Value = new Codeer.LowCode.Blazor.Repository.StringValue { Value = "1" } }) }, 0);
            var module = await ModuleCreationService.CreateModuleAsync(client.Core, page.Items.Single(), ModuleLayoutType.None);
            var field = Tags(module);
            Assert.That(field.Tags, Is.Empty, "まだ読んでいない");

            await field.AddTagAsync("展示会, セミナー");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "DXPO", "セミナー" }), "読んでから足す");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.StartWith("A:展示会 A:DXPO A:セミナー B:"));

            var other = await ModuleCreationService.CreateModuleAsync(client.Core, (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0)).Items.First(e => ((TextFieldData)e.Fields["Name"]).Value == "B"), ModuleLayoutType.None);
            await Tags(other).LoadTagsAsync();
            Assert.That(Tags(other).Tags, Is.EqualTo(new[] { "展示会" }));
        }

        [Test]
        public async Task 必須ならタグが1つも無いと保存できない()
        {
            _design = TagTestDesigns.Create(e => e.IsRequired = true);
            var module = await Client().OpenAsync("Contact", "4");
            Assert.That(await Tags(module).ValidateInput(), Is.False);
            await Tags(module).AddTagAsync("展示会");
            Assert.That(await Tags(module).ValidateInput(), Is.True);
        }

        #endregion

        #region 検索

        //画面の検索欄と同じく TagField に検索の値を入れ、その条件でサーバーの一覧を読む
        async Task<List<string>> NamesAsync(Func<TagField, Task> search, Action<TagField>? check = null)
        {
            var module = await Client().CreateNewAsync("Contact", ModuleLayoutType.Search);
            var field = Tags(module);
            await search(field);
            check?.Invoke(field);
            var condition = field.GetMatchCondition();
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact", Condition = condition == null ? new MultiMatchCondition() : MultiMatchCondition.And(condition) }, 0);
            return page.Items.Select(e => ((TextFieldData)e.Fields["Name"]).Value!).OrderBy(e => e).ToList();
        }

        [Test]
        public async Task すべて含むといずれかを含む()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会"])), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "DXPO"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(async f =>
            {
                await f.SetSearchTagsAsync(["DXPO", "セミナー"]);
                await f.SetSearchMatchAsync(TagSearchMatch.Any);
            }), Is.EqualTo(new[] { "A", "C" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["DXPO", "セミナー"])), Is.Empty);
        }

        [Test]
        public async Task タグは丸ごと一致で大文字小文字は区別しない()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示"])), Is.Empty, "部分一致はしない");
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["dxpo"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["100%達成"])), Is.Empty, "% は文字のまま (誰にも付いていない)");
        }

        [Test]
        public async Task マスタに無いタグはどのレコードにも合わない()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["無いタグ"])), Is.Empty);
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "無いタグ"])), Is.Empty, "すべて含む");
            Assert.That(await NamesAsync(async f =>
            {
                await f.SetSearchTagsAsync(["展示会", "無いタグ"]);
                await f.SetSearchMatchAsync(TagSearchMatch.Any);
            }), Is.EqualTo(new[] { "A", "B" }), "いずれか: 無いタグは足しにならない");
        }

        [Test]
        public async Task タグを選ばなければ絞らない()
            => Assert.That(await NamesAsync(f => f.SetSearchTagsAsync([]), f => Assert.That(f.GetMatchCondition(), Is.Null)), Is.EqualTo(new[] { "A", "B", "C", "D" }));

        [Test]
        public async Task 条件からタグと一致を復元できる()
        {
            var client = Client();
            var source = Tags(await client.CreateNewAsync("Contact", ModuleLayoutType.Search));
            await source.SetSearchTagsAsync(["展示会", "DXPO"]);
            var condition = (FieldMatchCondition)source.GetMatchCondition()!;

            var restored = Tags(await client.CreateNewAsync("Contact", ModuleLayoutType.Search));
            await ((ISearchableField)restored).SetMatchConditionAsync(condition);
            Assert.That(restored.SearchTags, Is.EqualTo(new[] { "展示会", "DXPO" }));
            Assert.That(restored.SearchMatch, Is.EqualTo(TagSearchMatch.All));

            await source.SetSearchMatchAsync(TagSearchMatch.Any);
            await ((ISearchableField)restored).SetMatchConditionAsync((FieldMatchCondition)source.GetMatchCondition()!);
            Assert.That(restored.SearchMatch, Is.EqualTo(TagSearchMatch.Any));

            await ((ISearchableField)restored).ClearMatchConditionAsync();
            Assert.That(restored.SearchTags, Is.Empty);
            Assert.That(((ISearchableField)restored).GetMatchCondition(), Is.Null);
        }

        #endregion

        #region 一覧の行・意味検索・一括ダウンロード

        [Test]
        public void 一覧の行はページの分をまとめて1回で読む()
        {
            var client = Client();
            var rows = new List<Module>();
            SingleThreadContext.Run(async () =>
            {
                var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0);
                var before = client.ListCalls.Count;
                foreach (var item in page.Items.OrderBy(e => ((TextFieldData)e.Fields["Name"]).Value))
                    rows.Add(await ModuleCreationService.CreateModuleAsync(client.Core, item, ModuleLayoutType.List));
                await Task.WhenAll(rows.Select(e => Tags(e).ListLoad!));
                Assert.That(client.ListCalls.Count - before, Is.EqualTo(1), "行ごとに問い合わせない: " + string.Join(" | ", client.ListCalls.Skip(before).Select(c => string.Join(",", c.Select(r => r.Condition.ModuleName + ":" + System.Text.Json.JsonSerializer.Serialize(r.Condition.Condition))))));
            });
            Assert.That(rows.Select(e => string.Join("+", Tags(e).Tags)), Is.EqualTo(new[] { "展示会+DXPO", "展示会", "セミナー", "" }));
            Assert.That(rows.Any(e => e.IsModified), Is.False);
            Assert.That(client.Logger.ErrorList, Is.Empty);
        }

        [Test]
        public async Task 意味検索の文章にタグ名が入る()
        {
            var field = new SemanticSearchFieldDesign { Name = "Search", SourceFields = ["Name", "Tags"] };
            var module = _design.Modules.Find("Contact")!;

            //クライアント (保存時): 画面のデータから
            var detail = await Client().OpenAsync("Contact", "1");
            Assert.That(SemanticSearchText.Build(_design, module, detail.GetData(), field), Is.EqualTo("名前: A\nタグ: 展示会, DXPO"));

            //サーバー (再索引): 一覧の読み込みにタグ付け行を足してから
            var io = CreateIO();
            var rows = (await io.GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0)).Items;
            await TagContracts.FillTagRowsAsync(_design, module, rows, ["Name", "Tags"], async (c, p) => await io.GetListAsync(c, p));
            var texts = rows.Select(e => SemanticSearchText.Build(_design, module, e, field)).OrderBy(e => e).ToList();
            Assert.That(texts, Is.EqualTo(new[] { "名前: A\nタグ: 展示会, DXPO", "名前: B\nタグ: 展示会", "名前: C\nタグ: セミナー", "名前: D" }));
        }

        [Test]
        public async Task 一括ダウンロードはTagFieldがあっても動きタグの列は出さない()
        {
            var file = await BulkFileTransfer.GetListFileAsync(_design, CreateIO(), new SearchCondition { ModuleName = "Contact" });
            file.Position = 0;
            var texts = await ExcelUtils.ReadAllTextsFromExcelBinary(file);
            Assert.That(texts[0], Does.Contain("Name.Value"));
            Assert.That(texts[0].Any(e => e.StartsWith("Tags")), Is.False, "header: " + string.Join(", ", texts[0]));
            Assert.That(texts, Has.Count.EqualTo(5));
        }

        #endregion
    }
}
