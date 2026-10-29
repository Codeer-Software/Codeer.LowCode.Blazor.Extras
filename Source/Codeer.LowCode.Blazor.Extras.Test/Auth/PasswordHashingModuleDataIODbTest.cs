using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// PasswordHashingModuleDataIO: 行を書く直前 (AddAsync / UpdateAsync / BulkAddAsync) に PasswordField の平文を、
    /// LoginAccountContractField / PasswordHashField のハッシュ + ソルトに置き換える。
    /// 実 DB (SQLite) で、これを継承した ModuleDataIO で書き、DB に入った値で確かめる (保存の Add / Update / 一括 INSERT と、システム権限の内部書き込み)。
    /// </summary>
    public class PasswordHashingModuleDataIODbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        static DesignData CreateDesign(bool useContract)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id", IsManualInput = true });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(new PasswordFieldDesign { Name = "Password" });   //平文の列は持たない
            if (useContract)
            {
                user.Fields.Add(new LoginAccountContractFieldDesign
                {
                    Name = "LoginAccount", LoginName = "UserName", PasswordField = "Password",
                    DbColumnPasswordHash = "hash", DbColumnPasswordSalt = "salt",
                });
            }
            else
            {
                user.Fields.Add(new PasswordHashFieldDesign { Name = "Hash", PasswordFieldName = "Password", DbColumnHash = "hash", DbColumnSalt = "salt" });
            }
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);
            return d;
        }

        //ホスト (テンプレートの CustomizedModuleDataIO) と同じ: PasswordHashingModuleDataIO を継承する
        class HostIO : PasswordHashingModuleDataIO
        {
            public HostIO(DesignData design, IAuthenticationContext auth, IDbAccessor db)
                : base(design, auth, db, new TemporaryFileManager(db, [], new List<IFileStorage>()))
            {
            }
        }

        //一括 INSERT の経路を通ったかを本体の事実 (ChangedRecords の IsBulkInsert) で見る
        class BulkProbe : IModuleDataIOInterceptor
        {
            public List<ChangedRecord> Changed { get; } = [];

            public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
            {
                var result = await next();
                Changed.AddRange(context.ChangedRecords);
                return result;
            }

            public Task GetListAsync(ModuleDataIO io, Repository.Match.SearchCondition condition, Codeer.LowCode.Blazor.Utils.Paging<ModuleData> result)
                => Task.CompletedTask;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"pw_hashing_io_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, hash TEXT, salt TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, user_name) VALUES ('U1','taro')", new());
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        HostIO CreateIO(bool useContract = true) => new(CreateDesign(useContract), this, _db);

        static ModuleData Row(string id, string? userName, string? password)
        {
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            if (userName != null) data.Fields["UserName"] = new TextFieldData { Value = userName };
            if (password != null) data.Fields["Password"] = new PasswordFieldData { Value = password };
            return data;
        }

        static ModuleSubmitData AddSubmit(string id, string userName, string password)
            => new() { ModuleName = "AppUser", Id = id, Add = [Row(id, userName, password)], NoTemporaryIdResolution = true };

        static ModuleSubmitData UpdateSubmit(string id, string? password, string? userName = null)
            => new() { ModuleName = "AppUser", Id = id, Update = [Row(id, userName, password)] };

        async Task<(string? Hash, string? Salt)> HashAsync(string id)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT hash, salt FROM app_users WHERE id = '{id}'", new())).Single();
            string? S(object? o) => o is null or DBNull ? null : o.ToString();
            return (S(row["hash"]), S(row["salt"]));
        }

        static void AssertNoError(List<ModuleSubmitResult> result)
            => Assert.That(result.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", result.Select(e => e.ExceptionMessage)));

        static void AssertHashOf(string password, (string? Hash, string? Salt) row)
        {
            Assert.That(row.Hash, Is.Not.Null.And.Not.Empty);
            Assert.That(row.Salt, Is.Not.Null.And.Not.Empty);
            Assert.That(row.Hash, Is.Not.EqualTo(password), "平文のまま入っていない");
            Assert.That(PasswordHashHelper.VerifyHash(password, row.Hash!, row.Salt!), Is.True);
            Assert.That(PasswordHashHelper.VerifyHash("wrong", row.Hash!, row.Salt!), Is.False);
        }

        [Test]
        public async Task 契約の追加でハッシュとソルトが入る()
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([AddSubmit("U2", "jiro", "secret2")]));
            AssertHashOf("secret2", await HashAsync("U2"));
        }

        [Test]
        public async Task 契約の更新でハッシュとソルトが入る()
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]));
            AssertHashOf("secret1", await HashAsync("U1"));
        }

        [Test]
        public async Task パスワードが空の更新ではハッシュを変えない()
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]));
            var before = await HashAsync("U1");

            //項目なし・空文字のどちらも変えない
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", password: null, userName: "taro2")]));
            Assert.That(await HashAsync("U1"), Is.EqualTo(before));
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", password: "", userName: "taro3")]));
            Assert.That(await HashAsync("U1"), Is.EqualTo(before));
            AssertHashOf("secret1", await HashAsync("U1"));
        }

        [Test]
        public async Task PasswordHashFieldの追加と更新でもハッシュとソルトが入る()
        {
            AssertNoError(await CreateIO(useContract: false).SubmitWithTransactionAsync([AddSubmit("U2", "jiro", "secret2")]));
            AssertHashOf("secret2", await HashAsync("U2"));

            AssertNoError(await CreateIO(useContract: false).SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]));
            AssertHashOf("secret1", await HashAsync("U1"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task 一括INSERTの経路でもハッシュとソルトが入る(bool useContract)
        {
            var saved = ModuleDataIO.BulkAddThreshold;
            ModuleDataIO.BulkAddThreshold = 2;
            var probe = new BulkProbe();
            try
            {
                var io = CreateIO(useContract);
                io.AddInterceptor(probe);
                AssertNoError(await io.SubmitWithTransactionAsync(
                [
                    AddSubmit("B1", "bulk1", "pass1"),
                    AddSubmit("B2", "bulk2", "pass2"),
                    AddSubmit("B3", "bulk3", "pass3"),
                ]));
            }
            finally
            {
                ModuleDataIO.BulkAddThreshold = saved;
            }
            Assert.That(probe.Changed.Count(e => e.IsBulkInsert), Is.EqualTo(3), "一括 INSERT の経路を通っている");
            AssertHashOf("pass1", await HashAsync("B1"));
            AssertHashOf("pass2", await HashAsync("B2"));
            AssertHashOf("pass3", await HashAsync("B3"));
        }

        //保存 (Submit) に載らない内部書き込みも本体の AddAsync / UpdateAsync を通るのでハッシュになる
        [Test]
        public async Task システム権限の内部書き込みでもハッシュとソルトが入る()
        {
            var system = CreateIO().CreateSystemAccess();
            await system.AddAsync(Row("S1", "saburo", "sys1"));
            AssertHashOf("sys1", await HashAsync("S1"));

            await system.UpdateAsync(Row("S1", null, "sys2"));
            AssertHashOf("sys2", await HashAsync("S1"));
        }
    }
}
