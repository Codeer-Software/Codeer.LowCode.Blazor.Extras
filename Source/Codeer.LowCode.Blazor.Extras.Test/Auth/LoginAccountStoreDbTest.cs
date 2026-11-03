using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>LoginAccountStore: ユーザーモジュールのデザイン (IdField + LoginAccountContractField の役割 + PasswordHashField) から表・列を引いて照合・解決する。</summary>
    public class LoginAccountStoreDbTest
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;

        static DesignData CreateDesign(string loginName = "UserName", string externalLoginName = "", string isActive = "", string displayName = "",
            bool withContract = true, bool withPasswordHash = true, string twoFactorEmail = "", string sessionStamp = "", string deleteField = "")
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
            //削除の印 (本体の論理削除のシステムフィールド)
            if (deleteField == SystemFieldNames.LogicalDelete) user.Fields.Add(new BooleanFieldDesign { Name = SystemFieldNames.LogicalDelete, DbColumn = "is_deleted" });
            if (deleteField == SystemFieldNames.DeletedAt) user.Fields.Add(new DateTimeFieldDesign { Name = SystemFieldNames.DeletedAt, DbColumn = "deleted_at" });
            if (deleteField == SystemFieldNames.Deleter) user.Fields.Add(new TextFieldDesign { Name = SystemFieldNames.Deleter, DbColumn = "deleter" });
            //PasswordHashField は「書く側」(ユーザー登録画面)。照合は契約の列で行う
            if (withPasswordHash) user.Fields.Add(new PasswordHashFieldDesign { Name = "Hash", PasswordFieldName = "Password", DbColumnHash = "hash", DbColumnSalt = "salt" });
            if (withContract) user.Fields.Add(new LoginAccountContractFieldDesign
            {
                Name = "LoginAccount", LoginName = loginName, ExternalLoginName = externalLoginName, IsActive = isActive, DisplayName = displayName, TwoFactorEmail = twoFactorEmail,
                DbColumnPasswordHash = withPasswordHash ? "hash" : "", DbColumnPasswordSalt = withPasswordHash ? "salt" : "",
                DbColumnSessionStamp = sessionStamp,
            });
            d.AddModule(user);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"loginaccount_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, user_name TEXT, email TEXT, name TEXT, is_active INTEGER, hash TEXT, salt TEXT, session_stamp TEXT, is_deleted INTEGER, deleted_at TEXT, deleter TEXT)", new());
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
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
            Assert.That(LoginAccountStore.Create(CreateDesign(withPasswordHash: false), _db)?.HasPassword, Is.False, "契約にパスワード列無し = 外部 IdP 専用");
            var half = CreateDesign(); half.Modules.Find("AppUser")!.Fields.OfType<LoginAccountContractFieldDesign>().First().DbColumnPasswordSalt = "";
            Assert.Throws<InvalidOperationException>(() => LoginAccountStore.Create(half, _db), "ハッシュとソルトは両方");

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

            //ホストが最初のユーザーを入れる経路。戻り値は作った行の Id
            Assert.That(await accounts.AddAsync("admin", "admin"), Is.EqualTo("1"));
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

            //総当たり対策が数える鍵: パスワードを照合せずにアカウントを引く
            Assert.That((await accounts.FindByLoginNameAsync("admin"))?.UserId, Is.EqualTo("1"));
            Assert.That(await accounts.FindByLoginNameAsync("nobody"), Is.Null);
            Assert.That(await accounts.FindByLoginNameAsync(null), Is.Null);
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
        public async Task TwoFactorEmail_Role()
        {
            var off = LoginAccountStore.Create(CreateDesign(), _db)!;
            Assert.That(off.HasTwoFactorEmail, Is.False);
            await InsertAsync("taro", "taro@example.co.jp", "Taro", 1, "p@ss");
            Assert.That((await off.VerifyPasswordAsync("taro", "p@ss"))!.TwoFactorEmail, Is.Null, "役割が無ければ null");

            var on = LoginAccountStore.Create(CreateDesign(twoFactorEmail: "Email"), _db)!;
            Assert.That(on.HasTwoFactorEmail, Is.True);
            Assert.That((await on.VerifyPasswordAsync("taro", "p@ss"))!.TwoFactorEmail, Is.EqualTo("taro@example.co.jp"));
            Assert.That((await on.FindByExternalLoginNameAsync("taro"))!.TwoFactorEmail, Is.EqualTo("taro@example.co.jp"));
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

        //---- セッションの印 (DbColumnSessionStamp) -----------------------------------------------

        async Task<string?> StampInDbAsync(string userName)
        {
            var row = (await _db.QueryAsync(Ds, "SELECT session_stamp FROM app_users WHERE user_name = @p1", new() { { "@p1", new Codeer.LowCode.Blazor.DataIO.Db.ParamAndRawDbTypeName { Value = userName } } })).Single();
            var v = row["session_stamp"];
            return v is null or DBNull ? null : v.ToString();
        }

        Task SetStampInDbAsync(string userName, string? stamp)
            => _db.ExecuteAsync(Ds, "UPDATE app_users SET session_stamp = @p1 WHERE user_name = @p2", new() { { "@p1", stamp }, { "@p2", userName } });

        //T01/T12: ログインの成立 (パスワード・外部 IdP) は印を返し、行の印が NULL なら採番して DB に書く。総当たり対策の引き (FindByLoginName) は書かない
        [Test]
        public async Task SessionStamp_LoginReturnsStamp_AndNullRowsAreStampedOnLogin()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(sessionStamp: "session_stamp"), _db)!;
            Assert.That(accounts.HasSessionStamp, Is.True);
            await InsertAsync("taro", "taro@example.co.jp", "Taro", 1, "p@ss");
            await InsertAsync("jiro", "jiro@example.co.jp", "Jiro", 1, "p@ss");
            Assert.That(await StampInDbAsync("taro"), Is.Null, "印を使い始める前からいるユーザー");

            //FindByLoginNameAsync は採番しない (DB は NULL のまま・返す印も null)
            Assert.That((await accounts.FindByLoginNameAsync("taro"))!.SessionStamp, Is.Null);
            Assert.That(await StampInDbAsync("taro"), Is.Null);

            //パスワードログインで採番され、返す印と DB の印が同じ
            var first = await accounts.VerifyPasswordAsync("taro", "p@ss");
            Assert.That(first!.SessionStamp, Is.Not.Null.And.Not.Empty);
            Assert.That(await StampInDbAsync("taro"), Is.EqualTo(first.SessionStamp));

            //印のある行はそのまま返す (ログインのたびに変えない = 他の端末の Cookie を切らない)
            var second = await accounts.VerifyPasswordAsync("taro", "p@ss");
            Assert.That(second!.SessionStamp, Is.EqualTo(first.SessionStamp));
            Assert.That((await accounts.FindByLoginNameAsync("taro"))!.SessionStamp, Is.EqualTo(first.SessionStamp), "印があれば FindByLoginName も返す");

            //パスワード不一致では採番しない
            Assert.That(await accounts.VerifyPasswordAsync("jiro", "wrong"), Is.Null);
            Assert.That(await StampInDbAsync("jiro"), Is.Null);

            //外部 IdP の解決も採番する
            var external = await accounts.FindByExternalLoginNameAsync("jiro");
            Assert.That(external!.SessionStamp, Is.Not.Null.And.Not.Empty);
            Assert.That(await StampInDbAsync("jiro"), Is.EqualTo(external.SessionStamp));
            Assert.That(external.SessionStamp, Is.Not.EqualTo(first.SessionStamp), "ユーザーごとに別の印");

            //空文字の印も NULL と同じく採番する
            await SetStampInDbAsync("jiro", "");
            var restamped = await accounts.VerifyPasswordAsync("jiro", "p@ss");
            Assert.That(restamped!.SessionStamp, Is.Not.Null.And.Not.Empty);
            Assert.That(await StampInDbAsync("jiro"), Is.EqualTo(restamped.SessionStamp));
        }

        //IsSessionValidAsync: 正しい印だけ true。別の印・空・null・存在しない Id・列の型に変換できない Id・停止中は false
        [Test]
        public async Task SessionStamp_IsSessionValid()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(isActive: "IsActive", sessionStamp: "session_stamp"), _db)!;
            await InsertAsync("taro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;

            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.True);
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, "other-stamp"), Is.False, "別の印");
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp!.ToUpperInvariant() + "X"), Is.False, "似た値も不一致");
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, ""), Is.False, "Cookie 側が空");
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, null), Is.False, "印の無い Cookie (列を足す前のログイン)");
            Assert.That(await accounts.IsSessionValidAsync("999", taro.SessionStamp), Is.False, "存在しない Id");
            Assert.That(await accounts.IsSessionValidAsync("abc", taro.SessionStamp), Is.False, "整数の Id 列に変換できない Id (壊れた Cookie)");
            Assert.That(await accounts.IsSessionValidAsync("", taro.SessionStamp), Is.False);
            Assert.That(await accounts.IsSessionValidAsync(null, taro.SessionStamp), Is.False);

            //行の印が空なら (照合では採番しない) 無効
            await SetStampInDbAsync("taro", null);
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.False);
            Assert.That(await StampInDbAsync("taro"), Is.Null, "照合は書かない");
            await SetStampInDbAsync("taro", taro.SessionStamp);
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.True);

            //停止中 (0 / NULL) は印が合っていても無効
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET is_active = 0 WHERE user_name = 'taro'", new());
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.False);
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET is_active = NULL WHERE user_name = 'taro'", new());
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.False);
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET is_active = 1 WHERE user_name = 'taro'", new());
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.True);
        }

        //T02/T03: 全端末ログアウト。古い印は落ち、戻り値の新しい印は通る。別ユーザーは影響を受けない。行が無ければ null
        [Test]
        public async Task SessionStamp_RevokeSessions()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(sessionStamp: "session_stamp"), _db)!;
            await InsertAsync("taro", null, null, 1, "p@ss");
            await InsertAsync("jiro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;
            var jiro = (await accounts.VerifyPasswordAsync("jiro", "p@ss"))!;

            var renewed = await accounts.RevokeSessionsAsync(taro.UserId);
            Assert.That(renewed, Is.Not.Null.And.Not.Empty);
            Assert.That(renewed, Is.Not.EqualTo(taro.SessionStamp));
            Assert.That(await StampInDbAsync("taro"), Is.EqualTo(renewed));
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.False, "失効前の Cookie は落ちる");
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, renewed), Is.True);
            Assert.That(await accounts.IsSessionValidAsync(jiro.UserId, jiro.SessionStamp), Is.True, "別ユーザーは影響を受けない");

            //新しいログインは止めない (新しい印で入れる)
            Assert.That((await accounts.VerifyPasswordAsync("taro", "p@ss"))!.SessionStamp, Is.EqualTo(renewed));

            Assert.That(await accounts.RevokeSessionsAsync("999"), Is.Null, "行が無い");
            Assert.That(await accounts.RevokeSessionsAsync("abc"), Is.Null, "変換できない Id");
            Assert.That(await accounts.RevokeSessionsAsync(""), Is.Null);
        }

        //T09: 行を消して同じ Id で作り直しても (印は NULL)、消す前の Cookie は通らない
        [Test]
        public async Task SessionStamp_RecreatedRowWithSameIdRejectsOldStamp()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(sessionStamp: "session_stamp"), _db)!;
            await InsertAsync("taro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;

            await _db.ExecuteAsync(Ds, "DELETE FROM app_users WHERE id = @p1", new() { { "@p1", long.Parse(taro.UserId) } });
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, user_name, is_active) VALUES (@p1, 'taro', 1)", new() { { "@p1", long.Parse(taro.UserId) } });
            Assert.That(await StampInDbAsync("taro"), Is.Null);
            Assert.That(await accounts.IsSessionValidAsync(taro.UserId, taro.SessionStamp), Is.False);
        }

        //印の列が無い契約: 照合なし (HasSessionStamp false)・ログインの印は null・照合 / 失効の API は例外
        [Test]
        public async Task SessionStamp_ContractWithoutColumn()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(), _db)!;
            Assert.That(accounts.HasSessionStamp, Is.False);
            await InsertAsync("taro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;
            Assert.That(taro.SessionStamp, Is.Null);
            Assert.That((await accounts.FindByExternalLoginNameAsync("taro"))!.SessionStamp, Is.Null);
            Assert.That(await StampInDbAsync("taro"), Is.Null, "列が契約に無ければ書かない");
            Assert.ThrowsAsync<InvalidOperationException>(() => accounts.IsSessionValidAsync(taro.UserId, "x"));
            Assert.ThrowsAsync<InvalidOperationException>(() => accounts.RevokeSessionsAsync(taro.UserId));
        }

        //---- 削除済みの行 (論理削除) -------------------------------------------------------------

        //ログインの 3 経路 + Cookie の照合が、その行を通すか (印は通る状態で取っておき、削除の印だけを変えて見る)
        async Task<bool[]> PassesAsync(LoginAccountStore accounts, string userName, string userId, string stamp)
            =>
            [
                await accounts.VerifyPasswordAsync(userName, "p@ss") != null,
                await accounts.FindByLoginNameAsync(userName) != null,
                await accounts.FindByExternalLoginNameAsync(userName) != null,
                await accounts.IsSessionValidAsync(userId, stamp),
            ];

        //LogicalDelete があれば 1 の行は削除済み (ログインも照合も通らない)。0 と NULL は通る
        [Test]
        public async Task Deleted_LogicalDeleteRowsAreExcluded()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(sessionStamp: "session_stamp", deleteField: SystemFieldNames.LogicalDelete), _db)!;
            await InsertAsync("taro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;
            var all = new[] { true, true, true, true };
            var none = new[] { false, false, false, false };

            Assert.That(await PassesAsync(accounts, "taro", taro.UserId, taro.SessionStamp!), Is.EqualTo(all), "NULL は削除されていない");
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET is_deleted = 1 WHERE user_name = 'taro'", new());
            Assert.That(await PassesAsync(accounts, "taro", taro.UserId, taro.SessionStamp!), Is.EqualTo(none), "削除済みは印が合っていても通らない");
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET is_deleted = 0 WHERE user_name = 'taro'", new());
            Assert.That(await PassesAsync(accounts, "taro", taro.UserId, taro.SessionStamp!), Is.EqualTo(all), "0 は削除されていない");
        }

        //LogicalDelete が無ければ DeletedAt、それも無ければ Deleter に値が入った行が削除済み
        [TestCase(SystemFieldNames.DeletedAt, "deleted_at", "2026-11-03 12:00:00")]
        [TestCase(SystemFieldNames.Deleter, "deleter", "admin")]
        public async Task Deleted_MarkerColumnRowsAreExcluded(string deleteField, string column, string value)
        {
            var accounts = LoginAccountStore.Create(CreateDesign(sessionStamp: "session_stamp", deleteField: deleteField), _db)!;
            await InsertAsync("taro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;

            await _db.ExecuteAsync(Ds, $"UPDATE app_users SET {column} = @p1 WHERE user_name = 'taro'", new() { { "@p1", value } });
            Assert.That(await PassesAsync(accounts, "taro", taro.UserId, taro.SessionStamp!), Is.EqualTo(new[] { false, false, false, false }));
            await _db.ExecuteAsync(Ds, $"UPDATE app_users SET {column} = NULL WHERE user_name = 'taro'", new());
            Assert.That(await PassesAsync(accounts, "taro", taro.UserId, taro.SessionStamp!), Is.EqualTo(new[] { true, true, true, true }));
        }

        //削除の印のフィールドが設計に無ければ、表に値が入っていても見ない (今までどおり)
        [Test]
        public async Task Deleted_WithoutDeleteFieldsColumnsAreIgnored()
        {
            var accounts = LoginAccountStore.Create(CreateDesign(sessionStamp: "session_stamp"), _db)!;
            await InsertAsync("taro", null, null, 1, "p@ss");
            var taro = (await accounts.VerifyPasswordAsync("taro", "p@ss"))!;
            await _db.ExecuteAsync(Ds, "UPDATE app_users SET is_deleted = 1, deleted_at = '2026-11-03', deleter = 'admin' WHERE user_name = 'taro'", new());
            Assert.That(await PassesAsync(accounts, "taro", taro.UserId, taro.SessionStamp!), Is.EqualTo(new[] { true, true, true, true }));
        }

        [Test]
        public void Contract_Defaults()
        {
            var contract = new LoginAccountContractFieldDesign();
            Assert.That(contract.LoginName, Is.Empty, "必須の役割も空が初期値 (空なら必須チェックが「指定してください」を出す)");
            Assert.That(contract.ExternalLoginName, Is.Empty);
            Assert.That(contract.IsActive, Is.Empty);
            Assert.That(contract.DisplayName, Is.Empty);
            Assert.That(contract.TwoFactorEmail, Is.Empty);
            Assert.That(contract.HasPassword, Is.False);
            Assert.That(contract.HasTotp, Is.False);
            Assert.That(contract.DbColumnSessionStamp, Is.Empty);
            Assert.That(contract.HasSessionStamp, Is.False);
        }
    }
}
