using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Mention;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Api
{
    /// <summary>
    /// メンションの候補 (共通の API の Mention.Candidates) を実 DB (SQLite) で検証する。
    /// ユーザー: 1=山田 (yamada) / 2=佐藤 (sato) / 3=鈴木 (suzuki・停止) / 4=表示名なし (tanaka) / 5=加藤 (kato・Rank 1)
    /// モジュール Item: UserReadCondition = Rank &gt;= 5。RichTextField "Body" はメンションあり、"Plain" はなし。
    /// </summary>
    public class MentionApiDbTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _currentUserId = "1";
        DesignData _designData = null!;

        public async Task<string> GetCurrentUserIdAsync()
        {
            await Task.CompletedTask;
            return _currentUserId;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"mention_api_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });
            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, LoginName TEXT, DisplayName TEXT, Rank INTEGER, IsActive INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1', 'yamada', '山田 太郎', 10, 1), ('2', 'sato', '佐藤 花子', 10, 1), ('3', 'suzuki', '鈴木 一郎', 10, 0), "
                + "('4', 'tanaka', NULL, 10, 1), ('5', 'kato', '加藤 次郎', 1, 1)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Body TEXT, Plain TEXT)", new());
            _designData = CreateDesignData();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
        }

        #region ハーネス

        static FieldValueMatchCondition Match(string variable, MatchComparison comparison, MultiTypeValue value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = comparison,
            Value = value,
        };

        static DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "LoginName", DbColumn = "LoginName" });
            user.Fields.Add(new TextFieldDesign { Name = "DisplayName", DbColumn = "DisplayName" });
            user.Fields.Add(new NumberFieldDesign { Name = "Rank", DbColumn = "Rank" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsActive", DbColumn = "IsActive" });
            user.Fields.Add(new LoginAccountContractFieldDesign { Name = "Login", LoginName = "LoginName", DisplayName = "DisplayName", IsActive = "IsActive" });
            //一般の利用者はユーザーのモジュールを読めない (候補はシステムの権限で読むので出る)
            user.UserReadCondition.ModuleName = "AppUser";
            user.UserReadCondition.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(100));
            d.AddModule(user);

            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "Items" };
            item.UserReadCondition.ModuleName = "AppUser";
            item.UserReadCondition.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            item.Fields.Add(new RichTextFieldDesign { Name = "Body", DbColumn = "Body", EnableMention = true });
            item.Fields.Add(new RichTextFieldDesign { Name = "Plain", DbColumn = "Plain" });
            item.Fields.Add(new TextFieldDesign { Name = "Title" });
            d.AddModule(item);
            return d;
        }

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す
        ExtrasApiContext Context(string userId)
        {
            _currentUserId = userId;
            var io = new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            return new ExtrasApiContext { ModuleDataIO = io, DesignData = _designData, DbAccess = _db, UserId = userId };
        }

        async Task<List<MentionCandidate>> CandidatesAsync(string text, string userId = "1", string fieldName = "Body")
        {
            var api = new ExtrasApi { Handlers = { new MentionApi() } };
            var request = new ExtrasRequest
            {
                Feature = MentionApiNames.Feature,
                Action = MentionApiNames.Candidates,
                Payload = JsonConverterEx.SerializeObject(new MentionCandidatesRequest { ModuleName = "Item", FieldName = fieldName, Text = text }),
            };
            var response = await api.ExecuteAsync(request, Context(userId));
            return JsonConverterEx.DeserializeObject<MentionCandidatesResponse>(response!.Payload)!.Candidates;
        }

        #endregion

        [Test]
        public async Task 何も打っていなければアプリを使える人を表示名の昇順で()
        {
            var candidates = await CandidatesAsync(string.Empty);
            //表示名の無い人はログイン ID で出る (並びは表示名の列 = 空が先)。停止した鈴木は出ない
            Assert.That(candidates.Select(e => e.DisplayName), Is.EqualTo(new[] { "tanaka", "佐藤 花子", "加藤 次郎", "山田 太郎" }));
            Assert.That(candidates.Select(e => e.Id), Is.EqualTo(new[] { "4", "2", "5", "1" }));
            Assert.That(candidates[1].LoginName, Is.EqualTo("sato"));
        }

        [Test]
        public async Task 打った文字を表示名かログインIDに部分一致で当てる()
        {
            Assert.That((await CandidatesAsync("山")).Select(e => e.Id), Is.EqualTo(new[] { "1" }));
            Assert.That((await CandidatesAsync("SAT")).Select(e => e.Id), Is.EqualTo(new[] { "2" }));
            Assert.That((await CandidatesAsync("ka")).Select(e => e.Id), Is.EquivalentTo(new[] { "4", "5" }));
            Assert.That(await CandidatesAsync("suzuki"), Is.Empty);
        }

        [Test]
        public async Task 打った文字の百分率と下線は文字のまま()
        {
            Assert.That(await CandidatesAsync("%"), Is.Empty);
            Assert.That(await CandidatesAsync("_"), Is.Empty);
        }

        [Test]
        public async Task アプリアクセス条件を満たす人だけ_ユーザーのモジュールの項目()
        {
            _designData.AppSettings.AppAccessConditions.ModuleName = "AppUser";
            _designData.AppSettings.AppAccessConditions.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            Assert.That((await CandidatesAsync(string.Empty)).Select(e => e.Id), Is.EqualTo(new[] { "4", "2", "1" }));
        }

        [Test]
        public async Task アプリアクセス条件を満たす人だけ_モジュール名が空()
        {
            //モジュール名が空でも、本体はユーザーのモジュールの項目として判定する
            _designData.AppSettings.AppAccessConditions.ModuleName = string.Empty;
            _designData.AppSettings.AppAccessConditions.Condition = MultiMatchCondition.And(
                Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5)),
                Match("LoginName.Value", MatchComparison.NotEqual, MultiTypeValue.Create("tanaka")));
            Assert.That((await CandidatesAsync(string.Empty)).Select(e => e.Id), Is.EqualTo(new[] { "2", "1" }));
        }

        [TestCase("Item", "Rank.Value")]
        [TestCase("AppUser", "CurrentUser.Rank.Value")]
        public async Task 本体で誰も通らない形のアプリアクセス条件なら候補を出さない(string moduleName, string variable)
        {
            Assert.That(MentionCandidateQuery.TryCreateAppAccessCondition(_designData, "AppUser", out _), Is.True);
            //別のモジュールを対象にした条件・CurrentUser の変数は、本体のアプリアクセスの判定で誰も通らない
            _designData.AppSettings.AppAccessConditions.ModuleName = moduleName;
            _designData.AppSettings.AppAccessConditions.Condition = Match(variable, MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            Assert.That(MentionCandidateQuery.TryCreateAppAccessCondition(_designData, "AppUser", out _), Is.False);
            //問い合わせた人自身もアプリに入れないので、候補の組み立てだけを見る
            var context = Context("1");
            Assert.That(await MentionCandidateQuery.FindAsync(context.ModuleDataIO, _designData, string.Empty), Is.Empty);
        }

        [Test]
        public async Task 候補は10件まで()
        {
            for (var i = 0; i < 15; i++)
            {
                await _db.ExecuteAsync(Ds, $"INSERT INTO AppUsers VALUES ('u{i:00}', 'user{i:00}', 'ユーザー{i:00}', 10, 1)", new());
            }
            var candidates = await CandidatesAsync("user");
            Assert.That(candidates.Select(e => e.LoginName), Is.EqualTo(Enumerable.Range(0, 10).Select(i => $"user{i:00}")));
        }

        [Test]
        public async Task 停止した人が多くても10件そろえる()
        {
            //ページの先頭を停止した人で埋める (有効フラグは読んでから判定する)
            for (var i = 0; i < 60; i++)
            {
                await _db.ExecuteAsync(Ds, $"INSERT INTO AppUsers VALUES ('s{i:00}', 'off{i:00}', 'あ停止{i:00}', 10, 0)", new());
            }
            var candidates = await CandidatesAsync(string.Empty);
            Assert.That(candidates.Select(e => e.Id), Is.EqualTo(new[] { "4", "2", "5", "1" }));
        }

        [Test]
        public void フィールドを見られない人は拒否()
        {
            //加藤 (Rank 1) は Item を開けない
            Assert.CatchAsync<LowCodeException>(async () => await CandidatesAsync(string.Empty, userId: "5"));
        }

        [Test]
        public void メンションを使わないフィールドは拒否()
        {
            Assert.CatchAsync<LowCodeException>(async () => await CandidatesAsync(string.Empty, fieldName: "Plain"));
            Assert.CatchAsync<LowCodeException>(async () => await CandidatesAsync(string.Empty, fieldName: "Title"));
        }

        [Test]
        public async Task 契約が無ければ候補なし()
        {
            _designData.Modules.Find("AppUser")!.Fields.RemoveAll(e => e is LoginAccountContractFieldDesign);
            Assert.That(await CandidatesAsync(string.Empty), Is.Empty);
        }
    }
}
