using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
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
    /// 検索 (いずれか = In、すべて = ContainsAll。どちらも SQL。会社 → 社員 → タグ の 2 段も)、一覧の行への同梱、意味検索の文章、一括ダウンロード。
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
            SqliteTestDb.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        DbClientServices Client() => new(_design, CreateIO);

        static TagField Tags(Module module) => (TagField)module.GetField("Tags")!;

        static string NameOf(ModuleData row) => ((TextFieldData)row.Fields["Name"]).Value ?? string.Empty;

        async Task<string> LinksAsync()
        {
            var rows = await _db.QueryAsync(Ds, "SELECT c.name AS c, l.name AS t FROM contact_tags l JOIN contacts c ON c.id = l.owner_id ORDER BY c.name, l.id", new());
            return string.Join(" ", rows.Select(r => $"{r["c"]}:{r["t"]}"));
        }

        //スクリプトの ModuleSearcher.Select(e => e.Tags) と同じ: 読む列に TagField を指定すると、本体がタグ付け行を同梱する
        static SearchCondition ContactsWithTags() => new() { ModuleName = "Contact", SelectFields = ["Id", "Name", "Tags"] };

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
        public async Task 大文字小文字が違えば別のタグ()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "1");
            var field = Tags(module);
            await field.AddTagAsync("dxpo");
            await field.AddTagAsync("展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "DXPO", "dxpo" }), "打ったまま足す (使われている表記に寄せない)");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.StartWith("A:展示会 A:DXPO A:dxpo B:"));
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
        public async Task 新規レコードのタグはそのレコードのIdで保存する()
        {
            var client = Client();
            var module = await client.CreateNewAsync("Contact");
            await module.GetField<TextField>("Name")!.SetValueAsync("E");
            await Tags(module).AddTagAsync("DXPO 新規");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.Contain("E:DXPO E:新規"));

            //保存した後の足し外し: 保存した行が今の行なので読み直さず、二重にもならない
            var reads = client.ListCalls.SelectMany(e => e).Count(e => e.Condition.ModuleName == "ContactTags");
            await Tags(module).AddTagAsync("DXPO VIP");
            Assert.That(client.ListCalls.SelectMany(e => e).Count(e => e.Condition.ModuleName == "ContactTags"), Is.EqualTo(reads), "読み直さない");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.Contain("E:DXPO E:新規 E:VIP"));
        }

        [Test]
        public async Task レコードをコピーするとタグもコピーされ元のタグ付け行には触らない()
        {
            var client = Client();
            var module = await client.OpenAsync("Contact", "1");
            await module.CopyModuleAsync();
            Assert.That(module.IsNewData, Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "DXPO" }));

            await module.GetField<TextField>("Name")!.SetValueAsync("A2");
            await Tags(module).RemoveTagAsync("DXPO");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Is.EqualTo("A:展示会 A:DXPO A2:展示会 B:展示会 C:セミナー"), "コピーのタグは新しい行。元のレコードのタグは消えない");
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
        public async Task 同じレコードに同じタグは一意インデックスで入らない()
        {
            //フィールドは同じレコードの中で重ねない。一意インデックスはその歯止め (スクリプトの直接の書き込みなど)
            Assert.ThrowsAsync<SqliteException>(async () => await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, 'DXPO')", new()));
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, 'dxpo')", new());
            Assert.That(await LinksAsync(), Does.StartWith("A:展示会 A:DXPO A:dxpo"), "大文字小文字が違えば別のタグ");
        }

        [Test]
        public async Task スクリプトで読んだレコードはTagFieldを読む列に指定するとタグが付いてくる()
        {
            //ModuleSearcher で読んだレコードと同じ: レイアウトなしで作る
            var client = Client();
            var withTags = (await CreateIO().GetListAsync(ContactsWithTags(), 0)).Items.Single(e => NameOf(e) == "A");
            var module = await ModuleCreationService.CreateModuleAsync(client.Core, withTags, ModuleLayoutType.None);
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "DXPO" }), "Select(e => e.Tags) で同梱される");

            await Tags(module).AddTagAsync("展示会 セミナー");
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "DXPO", "セミナー" }), "付いているタグは重ねない");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.StartWith("A:展示会 A:DXPO A:セミナー B:"));

            //読む列に指定しなければ、タグ付け行は付いてこない (docs に書いたとおり)
            var withoutTags = (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0)).Items.Single(e => NameOf(e) == "B");
            Assert.That(withoutTags.Fields.ContainsKey("Tags"), Is.False);
        }

        [Test]
        public async Task タグ付け行を読んでいないレコードはほかのフィールドを保存しても読んだ扱いにならない()
        {
            //Select 無しで読んだレコードの名前だけ直して保存 → そのあと足す。保存は「読んだか」を変えないので、足す前に 1 回読んで二重にならない
            var client = Client();
            var row = (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0)).Items.Single(e => NameOf(e) == "A");
            var module = await ModuleCreationService.CreateModuleAsync(client.Core, row, ModuleLayoutType.None);
            int TagReads() => client.ListCalls.SelectMany(e => e).Count(e => e.Condition.ModuleName == "ContactTags");

            await module.GetField<TextField>("Name")!.SetValueAsync("A1");
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(TagReads(), Is.EqualTo(0), "名前の保存ではタグ付け行を読まない");
            Assert.That(await LinksAsync(), Does.StartWith("A1:展示会 A1:DXPO B:"), "タグは触っていない");

            await Tags(module).AddTagAsync("展示会 VIP");
            Assert.That(TagReads(), Is.EqualTo(1), "足す前に 1 回読む (保存を挟んでも)");
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "DXPO", "VIP" }));
            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.StartWith("A1:展示会 A1:DXPO A1:VIP B:"), "二重にならない");
        }

        [Test]
        public async Task タグ付け行を読んでいないレコードは足し外しの前に1回読み二重にならない()
        {
            //ModuleSearcher で Select(e => e.Tags) を付けずに読んだレコードと同じ: タグ付け行は同梱されない
            var client = Client();
            var row = (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0)).Items.Single(e => NameOf(e) == "A");
            var module = await ModuleCreationService.CreateModuleAsync(client.Core, row, ModuleLayoutType.None);
            int TagReads() => client.ListCalls.SelectMany(e => e).Count(e => e.Condition.ModuleName == "ContactTags");
            Assert.That(TagReads(), Is.EqualTo(0), "表示のためには読まない");

            await Tags(module).AddTagAsync("展示会 VIP");
            Assert.That(TagReads(), Is.EqualTo(1), "足す前に 1 回読む");
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "DXPO", "VIP" }), "付いている 展示会 は重ねない");
            await Tags(module).RemoveTagAsync("DXPO");
            Assert.That(TagReads(), Is.EqualTo(1), "読むのは 1 回だけ");

            Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.StartWith("A:展示会 A:VIP B:"), "二重にならず、一意インデックスのエラーにもならない");
        }

        #endregion

        #region 候補

        [Test]
        public async Task 候補はよく使われている順で打った文字を含むもの()
        {
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (4, 'セミナー')", new());
            var client = Client();
            var field = Tags(await client.OpenAsync("Contact", "4"));
            Assert.That(await field.GetCandidatesAsync("ー"), Is.EqualTo(new[] { "セミナー" }));
            Assert.That(await field.GetCandidatesAsync("示"), Is.EqualTo(new[] { "展示会" }));
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, 'DX推進'), (3, 'DX推進'), (4, 'DX推進'), (2, 'DXPO')", new());
            Assert.That(await field.GetCandidatesAsync("DX"), Is.EqualTo(new[] { "DX推進", "DXPO" }), "件数の多い順 (DX推進 3、DXPO 2)");
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (3, 'DXPO')", new());
            Assert.That(await field.GetCandidatesAsync("DX"), Is.EqualTo(new[] { "DXPO", "DX推進" }), "同じ件数 (3 と 3) なら名前順");
            Assert.That(client.AggregateCalls, Has.Count.EqualTo(4), "1 回の候補で問い合わせ 1 回");
            Assert.That(client.ListCalls.SelectMany(e => e).Any(e => e.Condition.ModuleName == "ContactTags" && e.Condition.Condition is MultiMatchCondition { Children.Count: 0 }), Is.False, "全件は読まない");
        }

        [Test]
        public async Task 候補は上位10件()
        {
            for (var i = 1; i <= 12; i++) await _db.ExecuteAsync(Ds, $"INSERT INTO contact_tags (owner_id, name) VALUES (4, '候補{i:00}')", new());
            var field = Tags(await Client().OpenAsync("Contact", "3"));
            Assert.That(await field.GetCandidatesAsync("候補"), Is.EqualTo(Enumerable.Range(1, 10).Select(i => $"候補{i:00}")));
        }

        [Test]
        public async Task 候補の打った文字の記号は文字のまま()
        {
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (4, '100%達成'), (4, '100X達成'), (4, 'a_b'), (4, 'aXb')", new());
            var field = Tags(await Client().OpenAsync("Contact", "3"));
            Assert.That(await field.GetCandidatesAsync("100%"), Is.EqualTo(new[] { "100%達成" }));
            Assert.That(await field.GetCandidatesAsync("a_b"), Is.EqualTo(new[] { "a_b" }));
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
            return page.Items.Select(NameOf).OrderBy(e => e).ToList();
        }

        static async Task Any(TagField field, params string[] tags)
        {
            await field.SetSearchTagsAsync(tags.ToList());
            await field.SetSearchMatchAsync(TagSearchMatch.Any);
        }

        [Test]
        public async Task すべて含むといずれかを含む()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会"])), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "DXPO"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(f => Any(f, "DXPO", "セミナー")), Is.EqualTo(new[] { "A", "C" }));
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
        public async Task 該当が50件でもすべて含むは50件返す()
        {
            //50 人に 展示会 と DXPO、5 人に 展示会 だけ。条件のパラメータはタグの数だけで、該当の件数に依存しない
            var sql = new System.Text.StringBuilder();
            for (var i = 1; i <= 55; i++) sql.Append($"INSERT INTO contacts (name) VALUES ('M{i:00}');");
            await _db.ExecuteAsync(Ds, sql.ToString(), new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) SELECT id, '展示会' FROM contacts WHERE name LIKE 'M%'", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) SELECT id, 'DXPO' FROM contacts WHERE name LIKE 'M%' AND CAST(substr(name, 2) AS INTEGER) <= 50", new());

            async Task<int> CountAsync(Func<TagField, Task> search)
            {
                var module = await Client().CreateNewAsync("Contact", ModuleLayoutType.Search);
                await search(Tags(module));
                var condition = new SearchCondition { ModuleName = "Contact", Condition = MultiMatchCondition.And(Tags(module).GetMatchCondition()!), LimitCount = 1000 };
                var page = await CreateIO().GetListAsync(condition, 0);
                return page.Items.Count(e => NameOf(e).StartsWith('M'));
            }
            Assert.That(await CountAsync(f => f.SetSearchTagsAsync(["展示会", "DXPO"])), Is.EqualTo(50));
            Assert.That(await CountAsync(f => Any(f, "展示会", "DXPO")), Is.EqualTo(55));
        }

        [Test]
        public async Task 会社の検索に社員のタグを置くとすべて含むは会社の人のタグを合わせて判定する()
        {
            //X: p1 = 展示会, DXPO / Y: p2 = 展示会, p3 = DXPO (別の人に分かれている) / Z: p4 = 展示会
            TagTestDesigns.AddCompany(_design);
            _design = TagTestDesigns.Reload(_design);
            await _db.ExecuteAsync(Ds, "INSERT INTO companies (name) VALUES ('X'), ('Y'), ('Z')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contacts (name, company_id) VALUES ('p1', 1), ('p2', 2), ('p3', 2), ('p4', 3)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) SELECT id, '展示会' FROM contacts WHERE name IN ('p1', 'p2', 'p4')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) SELECT id, 'DXPO' FROM contacts WHERE name IN ('p1', 'p3')", new());

            //会社の検索画面: LinkFieldNames の People.Tags (社員の TagField) に検索の値を入れる
            async Task<string> CompaniesAsync(TagSearchMatch match, params string[] tags)
            {
                var module = await Client().CreateNewAsync("Company", ModuleLayoutType.Search);
                //読み込みで本体が作った People.Tags (社員の TagField)
                var tagField = (TagField)module.GetField("People.Tags")!;
                await tagField.SetSearchTagsAsync(tags.ToList());
                await tagField.SetSearchMatchAsync(match);
                var condition = tagField.GetMatchCondition()!;
                Assert.That(((FieldValueMatchCondition)((FieldMatchCondition)condition).Children.Single()).SearchTargetVariable, Is.EqualTo("People.Tags.Name.Value"));
                var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Company", Condition = MultiMatchCondition.And(condition) }, 0);
                return string.Join(",", page.Items.Select(NameOf).OrderBy(e => e));
            }
            Assert.That(await CompaniesAsync(TagSearchMatch.All, "展示会", "DXPO"), Is.EqualTo("X,Y"), "Y は 2 人に分かれていても合う");
            Assert.That(await CompaniesAsync(TagSearchMatch.All, "DXPO"), Is.EqualTo("X,Y"));
            Assert.That(await CompaniesAsync(TagSearchMatch.Any, "展示会", "DXPO"), Is.EqualTo("X,Y,Z"));
            Assert.That(await CompaniesAsync(TagSearchMatch.All, "展示会", "無いタグ"), Is.Empty);
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
            Assert.That(page.Items.Select(NameOf).OrderBy(e => e), Is.EqualTo(new[] { "A", "B" }));
        }

        [Test]
        public async Task タグ名は丸ごとの完全一致()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示"])), Is.Empty, "部分一致はしない");
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["DXPO"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["dxpo"])), Is.Empty, "大文字小文字も区別する");
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["100%達成"])), Is.Empty, "% は文字のまま (誰にも付いていない)");
        }

        [Test]
        public async Task 付いていないタグはどのレコードにも合わない()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["無いタグ"])), Is.Empty);
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "無いタグ"])), Is.Empty, "すべて含む");
            Assert.That(await NamesAsync(f => Any(f, "展示会", "無いタグ")), Is.EqualTo(new[] { "A", "B" }), "いずれか: 無いタグは足しにならない");
        }

        [Test]
        public async Task タグを選ばなければ絞らない()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync([]), f => Assert.That(f.GetMatchCondition(), Is.Null)), Is.EqualTo(new[] { "A", "B", "C", "D" }));
            Assert.That(await NamesAsync(f => Any(f), f => Assert.That(f.GetMatchCondition(), Is.Null)), Is.EqualTo(new[] { "A", "B", "C", "D" }), "いずれかを含む でも");
        }

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
        public async Task 一覧の行のタグは一覧の読み込みに同梱されクライアントからは読まない()
        {
            var client = Client();
            //一覧ページの条件と同じく、一覧レイアウトのフィールドを読む列に指定する
            var page = await CreateIO().GetListAsync(ContactsWithTags(), 0);
            var before = client.ListCalls.Count;
            var rows = new List<Module>();
            foreach (var item in page.Items.OrderBy(NameOf))
                rows.Add(await ModuleCreationService.CreateModuleAsync(client.Core, item, ModuleLayoutType.List));
            Assert.That(client.ListCalls.Count - before, Is.EqualTo(0), "行ごとにも、まとめても問い合わせない: " + string.Join(" | ", client.ListCalls.Skip(before).Select(c => string.Join(",", c.Select(r => r.Condition.ModuleName)))));
            Assert.That(rows.Select(e => string.Join("+", Tags(e).Tags)), Is.EqualTo(new[] { "展示会+DXPO", "展示会", "セミナー", "" }));
            Assert.That(rows.Any(e => e.IsModified), Is.False);
            Assert.That(client.Logger.ErrorList, Is.Empty);
        }

        [Test]
        public async Task 読み直したデータを入れるとタグ付け行が差し替わる()
        {
            //一覧の行の読み直し (本体の SetDataAsync) と同じ: 同梱されたタグ付け行で置き換わる。同梱が無ければ変えない
            var client = Client();
            var module = await client.OpenAsync("Contact", "1");
            await _db.ExecuteAsync(Ds, "DELETE FROM contact_tags WHERE owner_id = 1 AND name = 'DXPO'", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, 'VIP')", new());

            var fresh = (await CreateIO().GetListAsync(ContactsWithTags(), 0)).Items.Single(e => NameOf(e) == "A");
            await module.SetDataAsync(fresh);
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "VIP" }));
            Assert.That(module.IsModified, Is.False, "読み直しは変更ではない");

            var withoutTags = (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact" }, 0)).Items.Single(e => NameOf(e) == "A");
            await module.SetDataAsync(withoutTags);
            Assert.That(Tags(module).Tags, Is.EqualTo(new[] { "展示会", "VIP" }), "タグ付け行が同梱されていなければ変えない");
        }

        [Test]
        public async Task 詳細の中の一覧の行にもタグが出て行から足し外しして保存できる()
        {
            //会社の詳細に社員の一覧 (列にタグ)。X: p1 = 展示会 / p2 = なし
            TagTestDesigns.AddCompany(_design);
            await _db.ExecuteAsync(Ds, "INSERT INTO companies (name) VALUES ('X')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contacts (name, company_id) VALUES ('p1', 1), ('p2', 1)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) SELECT id, '展示会' FROM contacts WHERE name = 'p1'", new());
            _design.Modules.Find("Company")!.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Name", "People" } };

            var client = Client();
            var company = await client.OpenAsync("Company", "1");
            var people = company.GetField<ListField>("People")!;
            var rows = people.Rows.OrderBy(e => e.GetField<TextField>("Name")!.Value).ToList();
            Assert.That(rows.Select(e => string.Join("+", Tags(e).Tags)), Is.EqualTo(new[] { "展示会", "" }), string.Join(" | ", client.Logger.ErrorList));

            await Tags(rows[1]).AddTagAsync("DXPO");
            await Tags(rows[0]).RemoveTagAsync("展示会");
            Assert.That(await company.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));
            Assert.That(await LinksAsync(), Does.Contain("p2:DXPO").And.Not.Contain("p1:"));
        }

        [Test]
        public async Task 意味検索の文章にタグ名が入る()
        {
            var field = new SemanticSearchFieldDesign { Name = "Search", SourceFields = ["Name", "Tags"] };
            var module = _design.Modules.Find("Contact")!;

            //クライアント (保存時): 画面のデータから
            var detail = await Client().OpenAsync("Contact", "1");
            Assert.That(SemanticSearchText.Build(_design, module, detail.GetData(), field), Is.EqualTo("名前: A\nタグ: 展示会, DXPO"));

            //サーバー (再索引): 読む列 = 文章にするフィールド + Id (SemanticSearchService と同じ)。タグ付け行は本体が同梱する
            var rows = (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact", SelectFields = ["Name", "Tags", "Id"] }, 0)).Items;
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
