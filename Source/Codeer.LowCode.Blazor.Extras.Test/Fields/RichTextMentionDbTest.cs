using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// RichTextField のメンションの候補 = 同じモジュールの一覧フィールド (レイアウトに置いていない ListField) の行。実 DB (SQLite) で、
    /// 一覧をいつ読むか (最初の問い合わせ・一覧の条件が変わったとき)、手元の行への絞り込み、候補にしない行を確かめる。
    /// データ: メモ 1 = 営業 / メモ 2 = 開発。ユーザー: 営業 = 山田 太郎 (yamada), 佐藤 花子 (Sato), 表示名なし (suzuki) / 開発 = 高橋 (takahashi) / 部署なし = 名前もログイン ID も無い人。
    /// </summary>
    public class RichTextMentionDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        ListFieldDesign _list = null!;
        RichTextFieldDesign _body = null!;

        string _userId = "1";

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult(_userId);

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"mention_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, login TEXT, dept TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE memos (id INTEGER PRIMARY KEY AUTOINCREMENT, dept TEXT, body TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (name, login, dept) VALUES ('山田 太郎', 'yamada', '営業'), ('佐藤 花子', 'Sato', '営業'), (NULL, 'suzuki', '営業'), ('高橋', 'takahashi', '開発'), (NULL, NULL, '営業')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO memos (dept, body) VALUES ('営業', ''), ('開発', '')", new());
            CreateDesign();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        //メモ (Memo) の一覧 Users は、メモと同じ部署のユーザーを Id の順に読む。一覧はレイアウトに置かない (全体設定のフィールドだけ)
        void CreateDesign()
        {
            _design = new DesignData();

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            user.Fields.Add(new TextFieldDesign { Name = "Login", DbColumn = "login" });
            user.Fields.Add(new TextFieldDesign { Name = "Dept", DbColumn = "dept" });
            user.ListLayouts[""] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Name" }, new ListElement { FieldName = "Login" }]] };
            _design.AddModule(user);
            _design.AppSettings.CurrentUserModuleDesignName = user.Name;

            var memo = new ModuleDesign { Name = "Memo", DataSourceName = Ds, DbTable = "memos" };
            memo.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            memo.Fields.Add(new TextFieldDesign { Name = "Dept", DbColumn = "dept" });
            _list = new ListFieldDesign
            {
                Name = "Users",
                SearchCondition = new SearchCondition
                {
                    ModuleName = "AppUser",
                    LimitCount = null,
                    Condition = MultiMatchCondition.And(new FieldVariableMatchCondition { SearchTargetVariable = "Dept.Value", Variable = "Dept.Value", Comparison = MatchComparison.Equal }),
                    SortConditions = { new SortCondition { Variable = "Id.Value" } },
                },
            };
            memo.Fields.Add(_list);
            _body = new RichTextFieldDesign
            {
                Name = "Body", DbColumn = "body", EnableMention = true,
                MentionListField = "Users", MentionDisplayNameField = "Name", MentionLoginNameField = "Login",
            };
            memo.Fields.Add(_body);
            memo.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Dept", "Body" } };
            _design.AddModule(memo);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        //ログインしている人 (既定は Id 1 = 営業の 山田 太郎)。ユーザーのモジュールがある設計では、今のユーザーの行が無いと本体は一覧を読ませない
        async Task<DbClientServices> ClientAsync(string userId = "1")
        {
            _userId = userId;
            var client = new DbClientServices(_design, CreateIO);
            //今のユーザーの行はシステムの権限で読む (ユーザーのモジュールを読めない人でも、本体は今のユーザーの行を持っている)
            client.CurrentUserData = (await CreateIO().CreateSystemAccess().GetListAsync(new SearchCondition
            {
                ModuleName = "AppUser",
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = "Id.Value", Comparison = MatchComparison.Equal, Value = new StringValue { Value = userId } }),
            }, 0)).Items.Single();
            return client;
        }

        static RichTextField Body(Module module) => (RichTextField)module.GetField("Body")!;

        static string Names(MentionCandidates candidates) => string.Join(",", candidates.Items.Select(e => e.DisplayName));

        [Test]
        public async Task 候補は一覧の行で一覧の並びのまま出る()
        {
            var client = await ClientAsync();
            var body = Body(await client.OpenAsync("Memo", "1"));
            var ret = await body.GetMentionCandidatesAsync(string.Empty);

            //表示名が空の人はログイン ID で出す。どちらも空の行は出さない。開発の人は一覧の条件で外れる
            Assert.That(Names(ret), Is.EqualTo("山田 太郎,佐藤 花子,suzuki"));
            Assert.That(ret.HasPeople, Is.True);
            Assert.That(ret.Items.Select(e => e.Id), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(ret.Items.Select(e => e.LoginName), Is.EqualTo(new[] { "yamada", "Sato", "suzuki" }));
            Assert.That(client.Logger.ErrorList, Is.Empty);
        }

        [Test]
        public async Task 画面を開いただけでは一覧を読まず最初の問い合わせで1回だけ読む()
        {
            var client = await ClientAsync();
            var body = Body(await client.OpenAsync("Memo", "1"));
            var opened = client.ListCalls.Count;

            await body.GetMentionCandidatesAsync(string.Empty);
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened + 1));
            Assert.That(client.ListCalls.Last().Single().Condition.ModuleName, Is.EqualTo("AppUser"));

            //打つたびには読まない (手元の行に当てる)
            await body.GetMentionCandidatesAsync("山");
            await body.GetMentionCandidatesAsync("山田");
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened + 1));
        }

        [Test]
        public async Task 読んでいる間に来た問い合わせは同じ読み込みを待つ()
        {
            var client = await ClientAsync();
            var body = Body(await client.OpenAsync("Memo", "1"));
            var opened = client.ListCalls.Count;

            var first = body.GetMentionCandidatesAsync("山");
            var second = body.GetMentionCandidatesAsync("佐");
            Assert.That(Names(await first), Is.EqualTo("山田 太郎"));
            Assert.That(Names(await second), Is.EqualTo("佐藤 花子"));
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened + 1));
        }

        [TestCase("山田", "山田 太郎")]
        [TestCase("花", "佐藤 花子")]
        //ログイン ID にも当てる。大文字と小文字は区別しない
        [TestCase("YAMA", "山田 太郎")]
        [TestCase("sato", "佐藤 花子")]
        [TestCase("s", "佐藤 花子,suzuki")]
        [TestCase("いない", "")]
        public async Task 打った文字を表示名とログインIDに部分一致で当てる(string text, string expected)
        {
            var body = Body(await (await ClientAsync()).OpenAsync("Memo", "1"));
            var ret = await body.GetMentionCandidatesAsync(text);
            Assert.That(Names(ret), Is.EqualTo(expected));
            //「全員」を出すかは絞る前の人数で決まる
            Assert.That(ret.HasPeople, Is.True);
        }

        [Test]
        public async Task 候補は先頭から10件まで()
        {
            for (var i = 0; i < 15; i++)
                await _db.ExecuteAsync(Ds, $"INSERT INTO app_users (name, login, dept) VALUES ('追加{i:00}', 'add{i:00}', '営業')", new());
            var body = Body(await (await ClientAsync()).OpenAsync("Memo", "1"));

            var ret = await body.GetMentionCandidatesAsync("追加");
            Assert.That(ret.Items.Count, Is.EqualTo(RichTextField.MentionCandidateLimit));
            Assert.That(ret.Items.First().DisplayName, Is.EqualTo("追加00"));
            Assert.That(ret.Items.Last().DisplayName, Is.EqualTo("追加09"));
        }

        [Test]
        public async Task 一覧の条件に使っている項目が変わったら読み直す()
        {
            var client = await ClientAsync();
            var module = await client.OpenAsync("Memo", "1");
            var body = Body(module);
            Assert.That(Names(await body.GetMentionCandidatesAsync(string.Empty)), Does.StartWith("山田 太郎"));
            var loaded = client.ListCalls.Count;

            //レイアウトに置いていない一覧は、条件の項目が変わっても本体が読み直さない。次の問い合わせで読み直す
            await ((TextField)module.GetField("Dept")!).SetValueAsync("開発");
            Assert.That(client.ListCalls.Count, Is.EqualTo(loaded));
            Assert.That(Names(await body.GetMentionCandidatesAsync(string.Empty)), Is.EqualTo("高橋"));
            Assert.That(client.ListCalls.Count, Is.EqualTo(loaded + 1));

            await body.GetMentionCandidatesAsync("高");
            Assert.That(client.ListCalls.Count, Is.EqualTo(loaded + 1));
        }

        [Test]
        public async Task CurrentUserの条件は問い合わせた人の値で絞る()
        {
            ((FieldVariableMatchCondition)((MultiMatchCondition)_list.SearchCondition.Condition!).Children[0]).Variable = "CurrentUser.Dept.Value";
            var client = await ClientAsync("4");

            //新規作成の画面でも出る (編集中のレコードの Id を使わない条件)
            var body = Body(await client.CreateNewAsync("Memo"));
            Assert.That(Names(await body.GetMentionCandidatesAsync(string.Empty)), Is.EqualTo("高橋"));
        }

        [Test]
        public async Task 編集中のレコードのIdを使う条件は未保存のレコードでは読まない()
        {
            ((FieldVariableMatchCondition)((MultiMatchCondition)_list.SearchCondition.Condition!).Children[0]).SearchTargetVariable = "Id.Value";
            ((FieldVariableMatchCondition)((MultiMatchCondition)_list.SearchCondition.Condition!).Children[0]).Variable = "Id.Value";
            var client = await ClientAsync();
            var body = Body(await client.CreateNewAsync("Memo"));
            var opened = client.ListCalls.Count;

            var ret = await body.GetMentionCandidatesAsync(string.Empty);
            Assert.That(ret.Items, Is.Empty);
            Assert.That(ret.HasPeople, Is.False);
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened));

            //保存済みのレコードでは読む (メモ 2 と同じ Id のユーザー)
            var saved = Body(await client.OpenAsync("Memo", "2"));
            Assert.That(Names(await saved.GetMentionCandidatesAsync(string.Empty)), Is.EqualTo("佐藤 花子"));
        }

        [Test]
        public async Task 読み込みを止めた一覧では候補を出さない()
        {
            var client = await ClientAsync();
            var module = await client.OpenAsync("Memo", "1");
            var opened = client.ListCalls.Count;
            ((ListField)module.GetField("Users")!).AllowLoad = false;

            var ret = await Body(module).GetMentionCandidatesAsync(string.Empty);
            Assert.That(ret.Items, Is.Empty);
            Assert.That(ret.HasPeople, Is.False);
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened));
        }

        [Test]
        public async Task スクリプトで一覧を読み直した行を使う()
        {
            var client = await ClientAsync();
            var module = await client.OpenAsync("Memo", "1");
            var body = Body(module);
            await body.GetMentionCandidatesAsync(string.Empty);

            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (name, login, dept) VALUES ('新人', 'newcomer', '営業')", new());
            Assert.That(Names(await body.GetMentionCandidatesAsync("新")), Is.Empty);
            await ((ListField)module.GetField("Users")!).ReloadAsync();
            Assert.That(Names(await body.GetMentionCandidatesAsync("新")), Is.EqualTo("新人"));
        }

        [Test]
        public async Task 長すぎる文字では一覧を読まず候補も出さない()
        {
            var client = await ClientAsync();
            var body = Body(await client.OpenAsync("Memo", "1"));
            var opened = client.ListCalls.Count;

            var ret = await body.GetMentionCandidatesAsync(new string('a', RichTextField.MaxMentionTextLength + 1));
            Assert.That(ret.Items, Is.Empty);
            Assert.That(ret.HasPeople, Is.False);
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened));
        }

        [Test]
        public async Task メンションを使わない設定では一覧を読まない()
        {
            _body.EnableMention = false;
            var client = await ClientAsync();
            var body = Body(await client.OpenAsync("Memo", "1"));
            var opened = client.ListCalls.Count;

            Assert.That((await body.GetMentionCandidatesAsync(string.Empty)).Items, Is.Empty);
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened));
        }

        [Test]
        public async Task 一覧の指定が無ければ候補を出さない()
        {
            _body.MentionListField = string.Empty;
            var ret = await Body(await (await ClientAsync()).OpenAsync("Memo", "1")).GetMentionCandidatesAsync(string.Empty);
            Assert.That(ret.Items, Is.Empty);
            Assert.That(ret.HasPeople, Is.False);
        }

        [Test]
        public async Task 行のレイアウトに置いていない項目は読まれないので候補に出ない()
        {
            //一覧は行のレイアウトに置いた項目だけを読む。表示名もログイン ID も置き忘れると、誰も候補に出ない (設計チェックでは分からない)
            _design.Modules.Find("AppUser")!.ListLayouts[""] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Dept" }]] };
            var ret = await Body(await (await ClientAsync()).OpenAsync("Memo", "1")).GetMentionCandidatesAsync(string.Empty);
            Assert.That(ret.Items, Is.Empty);
            Assert.That(ret.HasPeople, Is.False);
        }

        #region 一般向けのユーザーモジュール (ユーザーのモジュールは限られた人だけが読める設計)

        //ユーザーのモジュール (AppUser) は開発の人だけが読める。同じ表を見る一般向けのモジュール (PublicUser・書き込めない・名前とログイン ID だけ) は誰でも読める
        void RestrictUserModule()
        {
            _design.Modules.Find("AppUser")!.UserReadCondition = new ModuleMatchCondition
            {
                ModuleName = "AppUser",
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = "Dept.Value", Comparison = MatchComparison.Equal, Value = new StringValue { Value = "開発" } }),
            };
            var publicUser = new ModuleDesign { Name = "PublicUser", DataSourceName = Ds, DbTable = "app_users", CanCreate = false, CanUpdate = false, CanDelete = false };
            publicUser.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            publicUser.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            publicUser.Fields.Add(new TextFieldDesign { Name = "Login", DbColumn = "login" });
            publicUser.Fields.Add(new TextFieldDesign { Name = "Dept", DbColumn = "dept" });
            publicUser.ListLayouts[""] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Name" }, new ListElement { FieldName = "Login" }]] };
            _design.AddModule(publicUser);
        }

        [Test]
        public async Task ユーザーのモジュールを読めない人には候補が出ない()
        {
            RestrictUserModule();
            //営業の人 (Id 1) は AppUser を読めない。本体は問い合わせずに一覧を空にする
            var client = await ClientAsync("1");
            var body = Body(await client.OpenAsync("Memo", "1"));
            var opened = client.ListCalls.Count;
            var ret = await body.GetMentionCandidatesAsync(string.Empty);
            Assert.That(ret.Items, Is.Empty);
            Assert.That(ret.HasPeople, Is.False);
            Assert.That(client.ListCalls.Count, Is.EqualTo(opened));

            //開発の人 (Id 4) は読めるので候補が出る
            var reader = Body(await (await ClientAsync("4")).OpenAsync("Memo", "1"));
            Assert.That(Names(await reader.GetMentionCandidatesAsync(string.Empty)), Is.EqualTo("山田 太郎,佐藤 花子,suzuki"));
        }

        [Test]
        public async Task 一般向けのユーザーモジュールを読む一覧ならユーザーのモジュールを読めない人にも候補が出る()
        {
            RestrictUserModule();
            _list.SearchCondition.ModuleName = "PublicUser";
            var noTables = new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>();
            Assert.That(_body.CheckDesign(new DesignCheckContext("Memo", _design, noTables)), Is.Empty);

            var client = await ClientAsync("1");
            var body = Body(await client.OpenAsync("Memo", "1"));
            var ret = await body.GetMentionCandidatesAsync(string.Empty);

            //Id はユーザーの表の Id (ユーザーのモジュールを読んだときと同じ値)
            Assert.That(Names(ret), Is.EqualTo("山田 太郎,佐藤 花子,suzuki"));
            Assert.That(ret.Items.Select(e => e.Id), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(client.ListCalls.Last().Single().Condition.ModuleName, Is.EqualTo("PublicUser"));
            Assert.That(client.Logger.ErrorList, Is.Empty);
        }

        #endregion

        [Test]
        public void テストのデザインは設計チェックを通る()
        {
            var noTables = new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>();
            Assert.That(_body.CheckDesign(new DesignCheckContext("Memo", _design, noTables)), Is.Empty);
        }
    }

    /// <summary>Example の MentionTest (メンションのサンプル画面) が読み込めて、デザインチェックを通ること。</summary>
    public class RichTextMentionSampleDesignTest
    {
        static string DesignDir
            => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Example", "Design"));

        //GetDesignData は App.zip を読む形式のため、リポジトリのデザインフォルダを一時 zip にして読む
        static DesignData Load()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"mention_design_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(DesignDir, Path.Combine(tempDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(tempDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Test]
        public void サンプルのMentionTestが読み込めてデザインチェックを通る()
        {
            var d = Load();
            var module = d.Modules.Find("MentionTest");
            Assert.That(module, Is.Not.Null);

            var noTables = new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>();
            var context = new DesignCheckContext("MentionTest", d, noTables);
            var fields = module!.Fields.OfType<RichTextFieldDesign>().ToList();
            Assert.That(fields.Select(e => e.MentionListField), Is.EqualTo(new[] { "MentionDeptUsers", "MentionAdminUsers" }));
            foreach (var field in fields)
            {
                Assert.That(field.EnableMention, Is.True);
                Assert.That(field.CheckDesign(context).Select(e => $"{e.Code} {e.Message}"), Is.Empty, field.Name);
                var list = module.Fields.OfType<ListFieldDesign>().Single(e => e.Name == field.MentionListField);
                Assert.That(list.CheckDesign(context).Select(e => $"{e.Code} {e.Message}"), Is.Empty, list.Name);
                //表示名とログイン ID の項目は、一覧の行のレイアウトに置いてある (置き忘れると候補が出ない)
                var columns = d.Modules.Find(list.SearchCondition.ModuleName)!.ListLayouts[list.LayoutName].Elements.SelectMany(e => e).Select(e => e.FieldName).ToList();
                Assert.That(columns, Does.Contain(field.MentionDisplayNameField).And.Contain(field.MentionLoginNameField));
            }
            //一般の利用者が @ を打つ本文の一覧は一般向けのユーザーモジュールを読む。管理者だけが読める本文の一覧は、ユーザーのモジュールを役割で絞って読む
            //(@ を打つのは管理者だけで、候補もその本文を読める人にする。役割は一般向けのモジュールには置かない)
            Assert.That(module.Fields.OfType<ListFieldDesign>().Select(e => (e.Name, e.SearchCondition.ModuleName)),
                Is.EqualTo(new[] { ("MentionDeptUsers", "NormalUser"), ("MentionAdminUsers", "AppUser") }));
            //一般向けのユーザーモジュール: ユーザーのモジュールと同じ表で、書き込めず、役割 (Role) とログインアカウント契約は置かない
            var user = d.Modules.Find(d.AppSettings.CurrentUserModuleDesignName)!;
            var normal = d.Modules.Find("NormalUser")!;
            Assert.That((normal.DataSourceName, normal.DbTable), Is.EqualTo((user.DataSourceName, user.DbTable)));
            Assert.That(normal.CanCreate || normal.CanUpdate || normal.CanDelete, Is.False);
            Assert.That(normal.Fields.Select(e => e.Name), Is.EqualTo(new[] { "Id", "Name", "Email", "Dept" }));
            var normalContext = new DesignCheckContext("NormalUser", d, noTables);
            Assert.That(normal.Fields.SelectMany(e => e.CheckDesign(normalContext)).Select(e => $"{e.Code} {e.Message}"), Is.Empty);
            //Home の RichText はメンションなし
            Assert.That(d.Modules.Find("Home")!.Fields.OfType<RichTextFieldDesign>().All(e => !e.EnableMention), Is.True);
        }
    }
}
