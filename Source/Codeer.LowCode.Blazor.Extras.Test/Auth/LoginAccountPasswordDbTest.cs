using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// LoginAccountContractField の PasswordField: 保存時にサーバー (PasswordHashHelper.ApplyPasswordHash) が平文をハッシュ / ソルトにして契約の列へ書く。
    /// 実 DB (SQLite) で、ホストと同じ経路 (ModuleDataIO の Add/Update で ApplyPasswordHash → 本体の書き込み) を通し、
    /// ログイン照合できること・TOTP の列には触れないこと・パスワード未入力なら変えないことを確認する。
    /// </summary>
    public class LoginAccountPasswordDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        static DesignData CreateDesign(string passwordField = "Password", bool withHashField = false, bool withColumns = true)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(new PasswordFieldDesign { Name = "Password" });   //平文の列は持たない
            if (withHashField) user.Fields.Add(new PasswordHashFieldDesign { Name = "Hash", PasswordFieldName = "Password", DbColumnHash = "hash", DbColumnSalt = "salt" });
            user.Fields.Add(new LoginAccountContractFieldDesign
            {
                Name = "LoginAccount", LoginName = "UserName", PasswordField = passwordField,
                DbColumnPasswordHash = withColumns ? "hash" : "", DbColumnPasswordSalt = withColumns ? "salt" : "",
                DbColumnTotpSecret = "totp_secret", DbColumnTotpConfirmed = "totp_confirmed", DbColumnTotpLastTimestep = "totp_last_timestep",
            });
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);
            return d;
        }

        //ホスト (テンプレートの CustomizedModuleDataIO) と同じ: 保存前に ApplyPasswordHash
        class HostIO : ModuleDataIO
        {
            readonly DesignData _design;

            public HostIO(DesignData design, IAuthenticationContext auth, IDbAccessor db)
                : base(design, auth, db, new TemporaryFileManager(db, [], new List<IFileStorage>()))
                => _design = design;

            protected override async Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
            {
                PasswordHashHelper.ApplyPasswordHash(_design.Modules.Find(data.Name)!, data);
                return await base.AddAsync(transactionId, moduleSubmitId, data);
            }

            protected override async Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
            {
                PasswordHashHelper.ApplyPasswordHash(_design.Modules.Find(data.Name)!, data);
                await base.UpdateAsync(transactionId, moduleSubmitId, data);
            }
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"login_pw_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, hash TEXT, salt TEXT, totp_secret TEXT, totp_confirmed INTEGER, totp_last_timestep INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users VALUES ('U1','taro',NULL,NULL,'SECRET1',1,12345)", new());
            _design = CreateDesign();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        HostIO CreateIO() => new(_design, this, _db);

        static ModuleSubmitData UpdateSubmit(string id, string? password, string? userName = null)
        {
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            if (userName != null) data.Fields["UserName"] = new TextFieldData { Value = userName };
            if (password != null) data.Fields["Password"] = new PasswordFieldData { Value = password };
            return new ModuleSubmitData { ModuleName = "AppUser", Id = id, Update = [data] };
        }

        async Task<(string? Hash, string? Salt, string? Secret, long Confirmed, long Step)> RowAsync(string userName)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT hash, salt, totp_secret, totp_confirmed, totp_last_timestep FROM app_users WHERE user_name = '{userName}'", new())).Single();
            var v = row.Values.ToList();
            string? S(object? o) => o is null or DBNull ? null : o.ToString();
            return (S(v[0]), S(v[1]), S(v[2]), Convert.ToInt64(v[3]), Convert.ToInt64(v[4]));
        }

        static void AssertNoError(List<ModuleSubmitResult> result)
            => Assert.That(result.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", result.Select(e => e.ExceptionMessage)));

        [Test]
        public async Task 更新でパスワードを入れると契約の列にハッシュが書かれTOTPは触らない()
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]));

            var row = await RowAsync("taro");
            Assert.That(row.Hash, Is.Not.Null.And.Not.Empty);
            Assert.That(row.Salt, Is.Not.Null.And.Not.Empty);
            Assert.That((row.Secret, row.Confirmed, row.Step), Is.EqualTo(("SECRET1", 1L, 12345L)));

            //サーバーのログイン照合で通る
            var store = LoginAccountStore.Create(_design, _db)!;
            Assert.That(store.HasPassword, Is.True);
            Assert.That((await store.VerifyPasswordAsync("taro", "secret1"))?.UserId, Is.EqualTo("U1"));
            Assert.That(await store.VerifyPasswordAsync("taro", "wrong"), Is.Null);
        }

        [Test]
        public async Task パスワード未入力の更新ではハッシュを変えない()
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", "secret1")]));
            var before = await RowAsync("taro");

            AssertNoError(await CreateIO().SubmitWithTransactionAsync([UpdateSubmit("U1", password: null, userName: "taro")]));
            var after = await RowAsync("taro");
            Assert.That((after.Hash, after.Salt), Is.EqualTo((before.Hash, before.Salt)));
            Assert.That((after.Secret, after.Confirmed, after.Step), Is.EqualTo(("SECRET1", 1L, 12345L)));
        }

        [Test]
        public async Task 新規登録でもハッシュが書かれる()
        {
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["Id"] = tempId;
            data.Fields["UserName"] = new TextFieldData { Value = "jiro" };
            data.Fields["Password"] = new PasswordFieldData { Value = "secret2" };
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "AppUser", Id = tempId.Value!, Add = [data] }]));

            var store = LoginAccountStore.Create(_design, _db)!;
            Assert.That(await store.VerifyPasswordAsync("jiro", "secret2"), Is.Not.Null);
        }

        [Test]
        public void 契約がハッシュを書くモジュールではPasswordHashFieldは飛ばす()
        {
            var design = CreateDesign(withHashField: true);
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["Password"] = new PasswordFieldData { Value = "secret" };
            PasswordHashHelper.ApplyPasswordHash(design.Modules.Find("AppUser")!, data);

            Assert.That(data.Fields["LoginAccount"], Is.InstanceOf<LoginAccountContractFieldData>());
            Assert.That(data.Fields.ContainsKey("Hash"), Is.False);

            //契約が PasswordField を持たなければ従来どおり PasswordHashField が書く
            var legacy = CreateDesign(passwordField: "", withHashField: true);
            var data2 = new ModuleData { Name = "AppUser" };
            data2.Fields["Password"] = new PasswordFieldData { Value = "secret" };
            PasswordHashHelper.ApplyPasswordHash(legacy.Modules.Find("AppUser")!, data2);
            Assert.That(data2.Fields["Hash"], Is.InstanceOf<PasswordHashFieldData>());
            Assert.That(data2.Fields.ContainsKey("LoginAccount"), Is.False);
        }

        List<string> CheckCodes(DesignData design)
        {
            var contract = design.Modules.Find("AppUser")!.Fields.OfType<LoginAccountContractFieldDesign>().Single();
            return contract.CheckDesign(new DesignCheckContext("AppUser", design, Utilities.CreateDataSource())).Select(e => e.Code).ToList();
        }

        [Test]
        public void デザインチェック()
        {
            //PasswordField が正しく指定されていれば契約固有の指摘は無い
            Assert.That(CheckCodes(CreateDesign()).Where(c => c.StartsWith(nameof(LoginAccountContractFieldDesign))), Is.Empty);

            //列が無い
            Assert.That(CheckCodes(CreateDesign(withColumns: false)), Does.Contain(DesignCheckCode.Create(typeof(LoginAccountContractFieldDesign), 3 /* PasswordFieldRequiresColumns */)));

            //PasswordHashField と併用
            Assert.That(CheckCodes(CreateDesign(withHashField: true)), Does.Contain(DesignCheckCode.Create(typeof(LoginAccountContractFieldDesign), 4 /* PasswordFieldConflictsWithHashField */)));

            //PasswordField 型でないフィールドを指すと本体の型チェックが指摘する
            Assert.That(CheckCodes(CreateDesign(passwordField: "UserName")).Any(c => c.StartsWith(nameof(DesignCheckContext))), Is.True);

            //TOTP の列は役割ではない (存在しないフィールド名として指摘されない)
            Assert.That(CheckCodes(CreateDesign()).Any(c => c.StartsWith(nameof(ContractFieldDesignBase))), Is.False);
        }
    }
}
