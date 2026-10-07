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
using Codeer.LowCode.Blazor.Repository;
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
    /// TagField を実 DB (SQLite) で: タグ付け行の読み込み・保存 (本体の保存に載る。保存までは何も書かない)、候補 (集計・よく使われている順)、
    /// 検索 (いずれか = In、すべて = ContainsAll。どちらも SQL)、一覧の行のまとめ読み、意味検索の文章、一括ダウンロード。
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
            var rows = await _db.QueryAsync(Ds, "SELECT c.name AS c, l.name AS t FROM contact_tags l JOIN contacts c ON c.id = l.owner_id ORDER BY c.name, l.id", new());
            return string.Join(" ", rows.Select(r => $"{r["c"]}:{r["t"]}"));
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
            Assert.That(client.SubmitCalls, Is.Empty, "足し外しでは何も書かない");

            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(client.SubmitCalls, Has.Count.EqualTo(1), "レコードの保存 1 回 (1 つのトランザクション)");
            Assert.That(await LinksAsync(), Is.EqualTo("A:展示会 A:DXPO B:セミナー C:セミナー"));
        }

        [Test]
        public async Task 保存せずにやめれば何も残らない()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            await Tags(module).AddTagAsync("打ち間違い");
            Assert.That(client.SubmitCalls, Is.Empty);
            Assert.That(await LinksAsync(), Is.EqualTo("A:展示会 A:DXPO B:展示会 C:セミナー"));
            var other = Tags(await client.OpenAsync("Contact", "3"));
            Assert.That(await other.GetCandidatesAsync("打ち"), Is.Empty, "候補にも出ない");
        }

        [Test]
        public async Task 大文字小文字違いは使われている表記に寄せ重ねない()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "2");
            var field = Tags(module);
            await field.AddTagAsync("dxpo");
            await field.AddTagAsync("展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "DXPO" }));
            Assert.That(field.HasTag("Dxpo"), Is.True);
            Assert.That(await module.SubmitAsync(), Is.True);
            Assert.That(await LinksAsync(), Is.EqualTo("A:展示会 A:DXPO B:展示会 B:DXPO C:セミナー"));
        }

        [Test]
        public async Task 新しいタグは保存で書き候補に出る()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            await Tags(module).AddTagAsync("新規A");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.Contain("D:新規A"));
            Assert.That(Tags(await client.OpenAsync("Contact", "4")).Tags, Is.EqualTo(new[] { "新規A" }));
            Assert.That(await Tags(await client.OpenAsync("Contact", "3")).GetCandidatesAsync("新規"), Is.EqualTo(new[] { "新規A" }));
        }

        [Test]
        public async Task 同じ新しいタグを複数のレコードに足して保存できる()
        {
            //取り込み・一括でタグを付ける画面: 何人もに同じ新しいタグを足して保存する
            var client = Client();
            var c = await client.OpenAsync("Contact", "3");
            var d = await client.OpenAsync("Contact", "4");
            await Tags(c).AddTagAsync("新規");
            await Tags(d).AddTagAsync("新規");
            Assert.That(await c.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await d.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.Contain("C:セミナー C:新規 D:新規"));
        }

        [Test]
        public async Task AllowNewTagsがfalseなら候補に無いタグは入らない()
        {
            _design = TagTestDesigns.Create(e => e.AllowNewTags = false);
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            var field = Tags(module);
            await field.AddTagAsync("新規");
            Assert.That(field.Tags, Is.Empty);
            Assert.That(field.IsValid, Is.False);
            Assert.That(field.ErrorText, Does.Contain("新規"));

            await field.AddTagAsync("dxpo");
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO" }));
            Assert.That(field.IsValid, Is.True, "足せたらエラーは消える");
        }

        [Test]
        public async Task 決まったタグだけの設定()
        {
            _design = TagTestDesigns.Create(e =>
            {
                e.CandidateSource = TagCandidateSource.Values;
                e.CandidateValues = "高\n中\n低";
                e.AllowNewTags = false;
            });
            var client = Client();
            var module = await client.OpenAsync("Contact", "4");
            await Tags(module).AddTagAsync("中, 展示会");
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "中" }), "使われているタグでも、決まったタグでなければ入らない");
            Assert.That(await module.SubmitAsync(), Is.True);
            Assert.That(await LinksAsync(), Does.Contain("D:中"));
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
        public async Task レコードを消せばタグ付け行も消える()
        {
            var io = CreateIO();
            var results = await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Contact", Id = "1", Delete = { new() { Id = "1", ModuleName = "Contact" } } }]);
            Assert.That(results.Select(e => e.ExceptionMessage).Where(e => !string.IsNullOrEmpty(e)), Is.Empty);
            Assert.That(await LinksAsync(), Is.EqualTo("B:展示会 C:セミナー"));
        }

        [Test]
        public void 同じレコードに大文字小文字違いの同じタグは一意インデックスで入らない()
        {
            //フィールドは同じレコードの中で重ねない。一意インデックスはその歯止め (スクリプトの直接の書き込みなど)
            Assert.ThrowsAsync<SqliteException>(async () => await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, 'dxpo')", new()));
        }

        [Test]
        public async Task スクリプトで読んだレコードでも付いているタグを重ねず足せる()
        {
            //ModuleSearcher で読んだレコードと同じ: レイアウトなしで作る (本体は子の一覧を読まない)
            var client = Client();
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact", Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = "Id.Value", Comparison = MatchComparison.Equal, Value = new StringValue { Value = "1" } }) }, 0);
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

        #region 候補

        [Test]
        public async Task 候補はよく使われている順で打った文字を含むもの()
        {
            var client = Client();
            var field = Tags(await client.OpenAsync("Contact", "4"));
            Assert.That(await field.GetCandidatesAsync(""), Is.EqualTo(new[] { "展示会", "DXPO", "セミナー" }), "件数の多い順、同じ件数は名前順");
            Assert.That(await field.GetCandidatesAsync("示"), Is.EqualTo(new[] { "展示会" }));
            Assert.That(await field.GetCandidatesAsync("dx"), Is.EqualTo(new[] { "DXPO" }));
            Assert.That(client.AggregateCalls, Has.Count.EqualTo(3), "1 回の問い合わせで 1 回分");
            Assert.That(client.ListCalls.SelectMany(e => e).Any(e => e.Condition.ModuleName == "ContactTags" && e.Condition.Condition is MultiMatchCondition { Children.Count: 0 }), Is.False, "全件は読まない");
        }

        [Test]
        public async Task 候補の打った文字の記号は文字のまま()
        {
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (4, '100%達成'), (4, '100X達成'), (4, 'a_b'), (4, 'aXb')", new());
            var field = Tags(await Client().OpenAsync("Contact", "3"));
            Assert.That(await field.GetCandidatesAsync("100%"), Is.EqualTo(new[] { "100%達成" }));
            Assert.That(await field.GetCandidatesAsync("a_b"), Is.EqualTo(new[] { "a_b" }));
        }

        [Test]
        public async Task 入力欄の候補は指定したTagFieldのタグ付けから()
        {
            var client = Client();
            var picker = (TagInputField)(await client.CreateNewAsync("Picker")).GetField("Pick")!;
            Assert.That(await picker.GetCandidatesAsync(""), Is.EqualTo(new[] { "展示会", "DXPO", "セミナー" }));
            Assert.That(client.AggregateCalls.Single().Single().ModuleName, Is.EqualTo("ContactTags"));
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
        public async Task すべて含むは3つ以上でもSQLで判定する()
        {
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, 'VIP'), (2, 'DXPO')", new());
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "DXPO"])), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "DXPO", "VIP"]), f =>
            {
                var value = (FieldValueMatchCondition)((FieldMatchCondition)f.GetMatchCondition()!).Children.Single();
                Assert.That(value.Comparison, Is.EqualTo(MatchComparison.ContainsAll));
                Assert.That(value.SearchTargetVariable, Is.EqualTo("Tags.Name.Value"));
                Assert.That(((ListValue<string>)value.Value!).Value, Is.EqualTo(new[] { "展示会", "DXPO", "VIP" }), "条件はタグ名 (レコードの Id の一覧ではない)");
            }), Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public async Task 条件を作った後に付いたタグも検索に出る()
        {
            var client = Client();
            var search = Tags(await client.CreateNewAsync("Contact", ModuleLayoutType.Search));
            await search.SetSearchTagsAsync(["展示会", "DXPO"]);
            var condition = search.GetMatchCondition()!;
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (2, 'DXPO')", new());
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact", Condition = MultiMatchCondition.And(condition) }, 0);
            Assert.That(page.Items.Select(e => ((TextFieldData)e.Fields["Name"]).Value).OrderBy(e => e), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public async Task タグは丸ごと一致で大文字小文字は区別しない()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示"])), Is.Empty, "部分一致はしない");
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["dxpo"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["dxpo", "展示会"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["100%達成"])), Is.Empty, "% は文字のまま (誰にも付いていない)");
        }

        [Test]
        public async Task 付いていないタグはどのレコードにも合わない()
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
            var calls = client.ListCalls.Count + client.AggregateCalls.Count;
            await ((ISearchableField)restored).SetMatchConditionAsync(condition);
            Assert.That(restored.SearchTags, Is.EqualTo(new[] { "展示会", "DXPO" }));
            Assert.That(restored.SearchMatch, Is.EqualTo(TagSearchMatch.All));
            Assert.That(client.ListCalls.Count + client.AggregateCalls.Count, Is.EqualTo(calls), "復元は問い合わせない (条件にタグ名がある)");

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
