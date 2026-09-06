using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>LoginAccountStore: ユーザーモジュールのデザイン (IdField + LoginAccountContractField の役割 + PasswordHashField) から表・列を引いて照合・解決する。</summary>
    public class LoginAccountStoreDbTest
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;

        static DesignData CreateDesign(string loginName = "UserName", string externalLoginName = "", string isActive = "", string displayName = "",
            bool withContract = true, bool withPasswordHash = true)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsActive", DbColumn = "is_active" });
            user.Fields.Add(new PasswordFieldDesign { Name = "Password" });
            if (withPasswordHash) user.Fields.Add(new PasswordHashFieldDesign { Name = "Hash", PasswordFieldName = "Password", DbColumnHash = "hash", DbColumnSalt = "salt" });
            if (withContract) user.Fields.Add(new LoginAccountContractFieldDesign { Name = "LoginAccount", LoginName = loginName, ExternalLoginName = externalLoginName, IsActive = isActive, DisplayName = displayName });
            d.AddModule(user);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"loginaccount_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, user_name TEXT, email TEXT, name TEXT, is_active INTEGER, hash TEXT, salt TEXT)", new());
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        async Task InsertAsync(string userName, string? email, string? name, int? isActive, string? password)
        {
            var hashed = password == null ? null : PasswordHashHelper.CreateHash(password);
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (user_name, email, name, is_active, hash, salt) VALUES (@p1, @p2, @p3, @p4, @p5, @p6)",
                new() { { "@p1", userName }, { "@p2", email }, { "@p3", name }, { "@p4", isActive }, { "@p5", hashed?.Hash ?? "" }, { "@p6", hashed?.Salt ?? "" } });
        }

        [Test]
        public void Create_RequiresContract_PasswordIsOptional_RolesAreValidated()
        {
            Assert.That(LoginAccountStore.Create(CreateDesign(), _db)?.HasPassword, Is.True);
            Assert.That(LoginAccountStore.Create(CreateDesign(withContract: false), _db), Is.Null, "契約が無ければ解決できない");
            Assert.That(LoginAccountStore.Create(CreateDesign(withPasswordHash: false), _db)?.HasPassword, Is.False, "PasswordHashField 無し = 外部 IdP 専用");

            var noUserModule = new DesignData();
            noUserModule.AppSettings.CurrentUserModuleDesignName = "Missing";
            Assert.That(LoginAccountStore.Create(noUserModule, _db), Is.Null);

            Assert.Throws<InvalidOperationException>(() => LoginAccountStore.Create(CreateDesign(loginName: ""), _db), "LoginName は必須");
            Assert.Throws<InvalidOperationException>(() => LoginAccountStore.Create(CreateDesign(loginName: "Password"), _db), "DB 列を持たないフィールドは不可");
            Assert.Throws<InvalidOperationException>(() => LoginAccountStore.Create(CreateDesign(displayName: "Nope"), _db), "任意の役割も指す先は要る");
            Assert.DoesNotThrow(() => LoginAccountStore.Create(CreateDesign(externalLoginName: "Email", isActive: "IsActive", displayName: "Name"), _db));
        }

        [Test]
        public async Task VerifyPassword_Find_Any_Add_MinimalContract()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(), _db)!;
            Assert.That(await accounts.AnyAsync(), Is.False);

            //初回起動の管理者作成と同じ経路
            await accounts.AddAsync("admin", "admin");
            Assert.That(await accounts.AnyAsync(), Is.True);

            var account = await accounts.VerifyPasswordAsync("admin", "admin");
            Assert.That(account, Is.Not.Null);
            Assert.That(account!.LoginName, Is.EqualTo("admin"));
            Assert.That(account.DisplayName, Is.EqualTo("admin"), "DisplayName 役割が無ければログイン ID");
            Assert.That(account.UserId, Is.EqualTo("1"), "Id 列の値 (自動採番)");

            Assert.That(await accounts.VerifyPasswordAsync("admin", "wrong"), Is.Null);
            Assert.That(await accounts.VerifyPasswordAsync("admin", ""), Is.Null);
            Assert.That(await accounts.VerifyPasswordAsync("nobody", "admin"), Is.Null);
            Assert.That(await accounts.VerifyPasswordAsync("", "admin"), Is.Null);
            Assert.That(await accounts.VerifyPasswordAsync(null, null), Is.Null);

            //ExternalLoginName 役割が無ければ LoginName の列で突き合わせる
            Assert.That((await accounts.FindByExternalLoginNameAsync("admin"))?.UserId, Is.EqualTo("1"));
            Assert.That(await accounts.FindByExternalLoginNameAsync("nobody"), Is.Null);
        }

        [Test]
        public async Task OptionalRoles_ExternalLoginName_IsActive_DisplayName()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(externalLoginName: "Email", isActive: "IsActive", displayName: "Name"), _db)!;
            await InsertAsync("taro", "taro@example.co.jp", "山田 太郎", 1, "p@ss");
            await InsertAsync("retired", "retired@example.co.jp", "退職 者", 0, "p@ss");
            await InsertAsync("nullactive", "n@example.co.jp", null, null, "p@ss");

            var taro = await accounts.VerifyPasswordAsync("taro", "p@ss");
            Assert.That(taro!.DisplayName, Is.EqualTo("山田 太郎"));
            Assert.That(taro.LoginName, Is.EqualTo("taro"));

            //外部 IdP はメール (ExternalLoginName) で突き合わせ、ログイン ID では見つけない
            Assert.That((await accounts.FindByExternalLoginNameAsync("taro@example.co.jp"))?.UserId, Is.EqualTo(taro.UserId));
            Assert.That(await accounts.FindByExternalLoginNameAsync("taro"), Is.Null);

            //停止ユーザーはどちらの経路でも入れない
            Assert.That(await accounts.VerifyPasswordAsync("retired", "p@ss"), Is.Null);
            Assert.That(await accounts.FindByExternalLoginNameAsync("retired@example.co.jp"), Is.Null);
            Assert.That(await accounts.VerifyPasswordAsync("nullactive", "p@ss"), Is.Null, "NULL は無効扱い");

            //管理者作成は表示名と有効フラグも埋める
            await accounts.AddAsync("admin", "admin");
            var admin = await accounts.VerifyPasswordAsync("admin", "admin");
            Assert.That(admin, Is.Not.Null);
            Assert.That(admin!.DisplayName, Is.EqualTo("admin"));
        }

        [Test]
        public async Task VerifyPassword_RejectsRowsWithoutHash()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(), _db)!;
            await InsertAsync("sso@example.com", null, "Sso", 1, null);
            Assert.That(await accounts.VerifyPasswordAsync("sso@example.com", ""), Is.Null);
            Assert.That(await accounts.VerifyPasswordAsync("sso@example.com", "anything"), Is.Null);
            Assert.That((await accounts.FindByExternalLoginNameAsync("sso@example.com"))?.LoginName, Is.EqualTo("sso@example.com"), "外部 IdP の解決はできる");
        }

        [Test]
        public async Task WithoutPasswordHashField_OnlyFindWorks()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(withPasswordHash: false), _db)!;
            await InsertAsync("sso@example.com", null, "Sso", 1, null);
            Assert.That((await accounts.FindByExternalLoginNameAsync("sso@example.com"))?.UserId, Is.EqualTo("1"));
            Assert.That(await accounts.AnyAsync(), Is.True);
            Assert.ThrowsAsync<InvalidOperationException>(() => accounts.VerifyPasswordAsync("sso@example.com", "x"));
            Assert.ThrowsAsync<InvalidOperationException>(() => accounts.AddAsync("admin", "admin"));
        }

        [Test]
        public void Contract_Defaults()
        {
            var contract = new LoginAccountContractFieldDesign();
            Assert.That(contract.LoginName, Is.EqualTo("LoginName"), "必須の役割だけ既定名 (既定名でフィールドを作れば置くだけで動く)");
            Assert.That(contract.ExternalLoginName, Is.Empty);
            Assert.That(contract.IsActive, Is.Empty);
            Assert.That(contract.DisplayName, Is.Empty);
        }
    }
}
