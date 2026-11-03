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

        /// <param name="sessionStamp">契約にセッションの印の列 (session_stamp) と有効フラグ (IsActive) を付け、同じ表を書く契約の無いモジュール (PasswordChange = PasswordHashField) も足す。</param>
        static DesignData CreateDesign(bool useContract, bool sessionStamp = false)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id", IsManualInput = true });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(new PasswordFieldDesign { Name = "Password" });   //平文の列は持たない
            if (sessionStamp) user.Fields.Add(new BooleanFieldDesign { Name = "IsActive", DbColumn = "is_active" });
            if (useContract)
            {
                user.Fields.Add(new LoginAccountContractFieldDesign
                {
                    Name = "LoginAccount", LoginName = "UserName", PasswordField = "Password",
                    DbColumnPasswordHash = "hash", DbColumnPasswordSalt = "salt",
                    IsActive = sessionStamp ? "IsActive" : "", DbColumnSessionStamp = sessionStamp ? "session_stamp" : "",
                });
            }
            else
            {
                user.Fields.Add(new PasswordHashFieldDesign { Name = "Hash", PasswordFieldName = "Password", DbColumnHash = "hash", DbColumnSalt = "salt" });
            }
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);
            if (sessionStamp)
            {
                //パスワード変更ダイアログなど、ユーザーモジュールと同じ表を書く契約の無いモジュール
                var change = new ModuleDesign { Name = "PasswordChange", DataSourceName = Ds, DbTable = "app_users" };
                change.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id", IsManualInput = true });
                change.Fields.Add(new PasswordFieldDesign { Name = "Password" });
                change.Fields.Add(new PasswordHashFieldDesign { Name = "Hash", PasswordFieldName = "Password", DbColumnHash = "hash", DbColumnSalt = "salt" });
                change.ListLayouts[""] = new ListLayoutDesign();
                d.AddModule(change);
            }
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
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, hash TEXT, salt TEXT, is_active INTEGER, session_stamp TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, user_name) VALUES ('U1','taro')", new());
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        HostIO CreateIO(bool useContract = true, bool sessionStamp = false) => new(CreateDesign(useContract, sessionStamp), this, _db);

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

        //---- セッションの印 (契約の DbColumnSessionStamp) --------------------------------------------

        //next() の後に例外を投げて保存全体をロールバックさせる
        class FailAfterWrite : IModuleDataIOInterceptor
        {
            public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
            {
                await next();
                throw new InvalidOperationException("fail after write");
            }

            public Task GetListAsync(ModuleDataIO io, Repository.Match.SearchCondition condition, Codeer.LowCode.Blazor.Utils.Paging<ModuleData> result)
                => Task.CompletedTask;
        }

        async Task<(string? Hash, string? Salt, string? Stamp)> AccountRowAsync(string id)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT hash, salt, session_stamp FROM app_users WHERE id = '{id}'", new())).Single();
            string? S(object? o) => o is null or DBNull ? null : o.ToString();
            return (S(row["hash"]), S(row["salt"]), S(row["session_stamp"]));
        }

        Task SetStampAsync(string id, string stamp)
            => _db.ExecuteAsync(Ds, $"UPDATE app_users SET session_stamp = '{stamp}', is_active = 1 WHERE id = '{id}'", new());

        static ModuleSubmitData Update(string module, string id, params (string Name, FieldDataBase Data)[] fields)
        {
            var data = new ModuleData { Name = module };
            data.Fields["Id"] = new IdFieldData { Value = id };
            foreach (var (name, value) in fields) data.Fields[name] = value;
            return new() { ModuleName = module, Id = id, Update = [data] };
        }

        //T04: 契約の PasswordField 経由のパスワード変更は、ハッシュ / ソルトと印を同じ保存で書き換える (保存とシステム権限の内部書き込みの両方)
        [Test]
        public async Task 印_契約のパスワード更新でハッシュと印が同時に変わる()
        {
            await SetStampAsync("U1", "S0");
            var io = CreateIO(sessionStamp: true);
            AssertNoError(await io.SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]));
            var after = await AccountRowAsync("U1");
            AssertHashOf("secret1", (after.Hash, after.Salt));
            Assert.That(after.Stamp, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("S0"));

            await io.CreateSystemAccess().UpdateAsync(Row("U1", null, "secret2"));
            var system = await AccountRowAsync("U1");
            AssertHashOf("secret2", (system.Hash, system.Salt));
            Assert.That(system.Stamp, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(after.Stamp));
        }

        //T05: 契約の無い別モジュール (同じ表を書く PasswordHashField) のパスワード変更でもユーザー行の印が変わる
        [Test]
        public async Task 印_同じ表を書く別モジュールのパスワード更新でも印が変わる()
        {
            await SetStampAsync("U1", "S0");
            var io = CreateIO(sessionStamp: true);
            AssertNoError(await io.SubmitWithTransactionAsync([Update("PasswordChange", "U1", ("Password", new PasswordFieldData { Value = "changed1" }))]));
            var after = await AccountRowAsync("U1");
            AssertHashOf("changed1", (after.Hash, after.Salt));
            Assert.That(after.Stamp, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("S0"));

            await io.CreateSystemAccess().UpdateAsync(Update("PasswordChange", "U1", ("Password", new PasswordFieldData { Value = "changed2" })).Update[0]);
            var system = await AccountRowAsync("U1");
            AssertHashOf("changed2", (system.Hash, system.Salt));
            Assert.That(system.Stamp, Is.Not.Null.And.Not.Empty.And.Not.EqualTo(after.Stamp));

            //パスワードを書かない保存では変えない
            AssertNoError(await io.SubmitWithTransactionAsync([Update("PasswordChange", "U1", ("Password", new PasswordFieldData { Value = "" }))]));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.EqualTo(system.Stamp));
        }

        //T10: 表示名など他の項目だけの保存では印を変えない (他の端末の Cookie を切らない)
        [Test]
        public async Task 印_表示名だけの更新では変わらない()
        {
            await SetStampAsync("U1", "S0");
            var io = CreateIO(sessionStamp: true);
            AssertNoError(await io.SubmitWithTransactionAsync([UpdateSubmit("U1", password: null, userName: "taro2")]));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.EqualTo("S0"));
            AssertNoError(await io.SubmitWithTransactionAsync([UpdateSubmit("U1", password: "", userName: "taro3")]));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.EqualTo("S0"));
            await io.CreateSystemAccess().UpdateAsync(Row("U1", "taro4", null));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.EqualTo("S0"));
        }

        //T08: 有効フラグを偽 (NULL も偽) にする保存で印が変わる。真に戻す保存では変えない (停止前の Cookie は戻らない)
        [Test]
        public async Task 印_停止で変わり有効に戻しても停止前の印には戻らない()
        {
            await SetStampAsync("U1", "S0");
            var io = CreateIO(sessionStamp: true);
            AssertNoError(await io.SubmitWithTransactionAsync([Update("AppUser", "U1", ("IsActive", new BooleanFieldData { Value = false }))]));
            var stopped = (await AccountRowAsync("U1")).Stamp;
            Assert.That(stopped, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("S0"));

            AssertNoError(await io.SubmitWithTransactionAsync([Update("AppUser", "U1", ("IsActive", new BooleanFieldData { Value = true }))]));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.EqualTo(stopped), "有効に戻しても停止時の印のまま");

            AssertNoError(await io.SubmitWithTransactionAsync([Update("AppUser", "U1", ("IsActive", new BooleanFieldData { Value = null }))]));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.Not.EqualTo(stopped), "NULL も停止扱い");
        }

        //新規行: 契約経由でパスワード付きなら印も入る。パスワード無しなら印は NULL (最初のログインで採番)
        [Test]
        public async Task 印_追加はパスワード付きなら入りパスワード無しならNULL()
        {
            var io = CreateIO(sessionStamp: true);
            AssertNoError(await io.SubmitWithTransactionAsync([AddSubmit("U2", "jiro", "secret2")]));
            var withPassword = await AccountRowAsync("U2");
            AssertHashOf("secret2", (withPassword.Hash, withPassword.Salt));
            Assert.That(withPassword.Stamp, Is.Not.Null.And.Not.Empty);

            AssertNoError(await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "AppUser", Id = "U3", Add = [Row("U3", "saburo", null)], NoTemporaryIdResolution = true }]));
            Assert.That((await AccountRowAsync("U3")).Stamp, Is.Null);
        }

        //T06: 印の書き換えは行の UPDATE と同じトランザクション。保存が失敗すればパスワードも印も元のまま (契約経由・PasswordHashField 経由の直接 UPDATE とも)
        [Test]
        public async Task 印_保存が失敗すればパスワードも印もロールバックされる()
        {
            await SetStampAsync("U1", "S0");
            var before = await AccountRowAsync("U1");
            var io = CreateIO(sessionStamp: true);
            io.AddInterceptor(new FailAfterWrite());

            var viaContract = await io.SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]);
            Assert.That(viaContract.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(await AccountRowAsync("U1"), Is.EqualTo(before));

            var viaHashField = await io.SubmitWithTransactionAsync([Update("PasswordChange", "U1", ("Password", new PasswordFieldData { Value = "changed1" }))]);
            Assert.That(viaHashField.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(await AccountRowAsync("U1"), Is.EqualTo(before));

            var viaInactive = await io.SubmitWithTransactionAsync([Update("AppUser", "U1", ("IsActive", new BooleanFieldData { Value = false }))]);
            Assert.That(viaInactive.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(await AccountRowAsync("U1"), Is.EqualTo(before));
        }

        //T11: クライアントが契約 / PasswordHashField のデータを送信に載せても使わない (ApplyPasswordHash が捨てる)。
        //平文が無ければハッシュ / ソルト / 印は変わらず、平文があればサーバーが作った値になる
        [Test]
        public async Task 印_送信に載った契約やハッシュのデータは使わない()
        {
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET hash = 'H0', salt = 'S0', session_stamp = 'T0', is_active = 1 WHERE id = 'U1'", new());
            var io = CreateIO(sessionStamp: true);
            var attacker = new Codeer.LowCode.Blazor.Extras.Data.LoginAccountContractFieldData { PasswordHash = "attacker", PasswordSalt = "attacker", SessionStamp = "attacker" };

            AssertNoError(await io.SubmitWithTransactionAsync([Update("AppUser", "U1", ("UserName", new TextFieldData { Value = "taro2" }), ("LoginAccount", attacker))]));
            Assert.That(await AccountRowAsync("U1"), Is.EqualTo(((string?)"H0", (string?)"S0", (string?)"T0")), "平文が無い保存では変わらない");

            await io.CreateSystemAccess().UpdateAsync(Update("AppUser", "U1", ("LoginAccount", attacker.Clone())).Update[0]);
            Assert.That(await AccountRowAsync("U1"), Is.EqualTo(((string?)"H0", (string?)"S0", (string?)"T0")), "内部書き込みでも同じ");

            AssertNoError(await io.SubmitWithTransactionAsync([Update("AppUser", "U1", ("Password", new PasswordFieldData { Value = "secret1" }), ("LoginAccount", attacker.Clone()))]));
            var withPassword = await AccountRowAsync("U1");
            AssertHashOf("secret1", (withPassword.Hash, withPassword.Salt));
            Assert.That(withPassword.Stamp, Is.Not.Null.And.Not.EqualTo("attacker").And.Not.EqualTo("T0"), "平文があればサーバーが作った印");

            //新規行に載せても同じ (パスワード無し = 何も入らない)
            var add = Row("U3", "saburo", null);
            add.Fields["LoginAccount"] = attacker.Clone();
            AssertNoError(await io.SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "AppUser", Id = "U3", Add = [add], NoTemporaryIdResolution = true }]));
            Assert.That(await AccountRowAsync("U3"), Is.EqualTo(((string?)null, (string?)null, (string?)null)));

            //PasswordHashField のデータを直接載せても同じ (別モジュール・ユーザーモジュールが契約無しの形の両方)
            var hashData = new Codeer.LowCode.Blazor.Extras.Data.PasswordHashFieldData { Hash = "attacker", Salt = "attacker" };
            AssertNoError(await io.SubmitWithTransactionAsync([Update("PasswordChange", "U1", ("Hash", hashData))]));
            Assert.That((await AccountRowAsync("U1")).Hash, Is.EqualTo(withPassword.Hash));
            Assert.That((await AccountRowAsync("U1")).Salt, Is.EqualTo(withPassword.Salt));
            Assert.That((await AccountRowAsync("U1")).Stamp, Is.EqualTo(withPassword.Stamp));

            AssertNoError(await CreateIO(useContract: false).SubmitWithTransactionAsync([Update("AppUser", "U1", ("Hash", new Codeer.LowCode.Blazor.Extras.Data.PasswordHashFieldData { Hash = "attacker", Salt = "attacker" }))]));
            Assert.That((await AccountRowAsync("U1")).Hash, Is.EqualTo(withPassword.Hash));
            Assert.That((await AccountRowAsync("U1")).Salt, Is.EqualTo(withPassword.Salt));
        }
    }
}
