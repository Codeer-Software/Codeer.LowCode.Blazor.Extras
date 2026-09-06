using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// TotpLogin の 2 段階の流れを実 DB (SQLite) で: 初回登録 → 確認 → 以降はコード要求 → リプレイ拒否 → リセット。
    /// 表・列はユーザーモジュールのデザイン (IdField + LoginAccountContractField の TOTP 列) から引く。
    /// </summary>
    public class TotpLoginDbTest
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        TotpLogin _totp = null!;

        static DesignData CreateDesign(bool withTotpField = true)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(withTotpField
                ? new LoginAccountContractFieldDesign { Name = "LoginAccount", LoginName = "UserName", DbColumnTotpSecret = "totp_secret", DbColumnTotpConfirmed = "totp_confirmed", DbColumnTotpLastTimestep = "totp_last_timestep" }
                : new LoginAccountContractFieldDesign { Name = "LoginAccount", LoginName = "UserName" });
            d.AddModule(user);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"totp_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, totp_secret TEXT, totp_confirmed INTEGER, totp_last_timestep INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, user_name) VALUES ('U1','taro'),('A','a'),('B','b')", new());
            _design = CreateDesign();
            _totp = TotpLogin.Create(_design, new TotpLoginSettings { Issuer = "Test App" }, _db)!;
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        static string CurrentCode(string secret) => Totp.ComputeCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Totp.PeriodSeconds);

        [Test]
        public void Create_RequiresTotpColumnsOnTheContract()
        {
            Assert.That(_totp, Is.Not.Null);
            Assert.That(TotpLogin.Create(CreateDesign(withTotpField: false), new(), _db), Is.Null, "契約に TOTP 列が無ければ認証アプリの二要素認証なし");

            var noUserModule = new DesignData();
            noUserModule.AppSettings.CurrentUserModuleDesignName = "Missing";
            Assert.That(TotpLogin.Create(noUserModule, new(), _db), Is.Null);
        }

        [Test]
        public void Ctor_RequiresColumns()
        {
            var module = _design.Modules.Find("AppUser")!;
            Assert.Throws<InvalidOperationException>(() => new TotpLogin(module, new LoginAccountContractFieldDesign { Name = "T", DbColumnTotpSecret = "s" }, new(), _db), "列未設定");
            var noId = new ModuleDesign { Name = "X", DataSourceName = Ds, DbTable = "app_users" };
            Assert.Throws<InvalidOperationException>(() => new TotpLogin(noId, module.Fields.OfType<LoginAccountContractFieldDesign>().First(), new(), _db), "IdField 無し");
        }

        [Test]
        public async Task FirstLogin_Setup_ThenConfirm_ThenCodeRequired()
        {
            //1. 未登録: 登録情報が返り、鍵はユーザー行に未確認で保存される
            var setup = await _totp.VerifyAsync("U1", "taro", null);
            Assert.That(setup.Status, Is.EqualTo(TotpLoginStatus.Setup));
            Assert.That(setup.Secret, Is.Not.Null.And.Length.EqualTo(32));
            Assert.That(setup.OtpauthUri, Does.StartWith("otpauth://totp/Test%20App:taro?secret=" + setup.Secret));
            Assert.That(Convert.FromBase64String(setup.QrPngBase64!).Take(4), Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), "PNG");
            var stored = await _totp.FindAsync("U1");
            Assert.That(stored!.IsConfirmed, Is.False);
            Assert.That(stored.Secret, Is.EqualTo(setup.Secret));
            var row = (await _db.QueryAsync(Ds, "select user_name, totp_secret from app_users where id = 'U1'", new())).Single();
            Assert.That(row["user_name"], Is.EqualTo("taro"), "他の列はそのまま");
            Assert.That(row["totp_secret"], Is.EqualTo(setup.Secret));

            //2. 確認前にもう一度パスワードを通すと、同じ鍵で QR を再表示する (登録し直しにならない)
            var again = await _totp.VerifyAsync("U1", "taro", "");
            Assert.That(again.Status, Is.EqualTo(TotpLoginStatus.Setup));
            Assert.That(again.Secret, Is.EqualTo(setup.Secret));

            //3. 間違ったコードでは確定しない
            Assert.That((await _totp.VerifyAsync("U1", "taro", "000000")).Status, Is.EqualTo(TotpLoginStatus.InvalidCode));
            Assert.That((await _totp.FindAsync("U1"))!.IsConfirmed, Is.False);

            //4. 正しいコードで確定
            var ok = await _totp.VerifyAsync("U1", "taro", CurrentCode(setup.Secret!));
            Assert.That(ok.Status, Is.EqualTo(TotpLoginStatus.Ok));
            var confirmed = await _totp.FindAsync("U1");
            Assert.That(confirmed!.IsConfirmed, Is.True);
            Assert.That(confirmed.LastTimestep, Is.GreaterThan(0));

            //5. 以降はコード要求 (登録情報は出さない)
            var next = await _totp.VerifyAsync("U1", "taro", null);
            Assert.That(next.Status, Is.EqualTo(TotpLoginStatus.CodeRequired));
            Assert.That(next.Secret, Is.Null);
            Assert.That(next.QrPngBase64, Is.Null);
        }

        [Test]
        public async Task SameCode_CannotBeReused()
        {
            var setup = await _totp.VerifyAsync("U1", "taro", null);
            var code = CurrentCode(setup.Secret!);
            Assert.That((await _totp.VerifyAsync("U1", "taro", code)).Status, Is.EqualTo(TotpLoginStatus.Ok));
            Assert.That((await _totp.VerifyAsync("U1", "taro", code)).Status, Is.EqualTo(TotpLoginStatus.InvalidCode), "同じステップのコードは 2 度使えない");
            Assert.That((await _totp.VerifyAsync("U1", "taro", " " + code + " ")).Status, Is.EqualTo(TotpLoginStatus.InvalidCode));
        }

        [Test]
        public async Task CodeWithoutRegistration_IsInvalid()
        {
            Assert.That((await _totp.VerifyAsync("U1", "taro", "123456")).Status, Is.EqualTo(TotpLoginStatus.InvalidCode));
            Assert.That(await _totp.FindAsync("U1"), Is.Null, "コードだけ送っても鍵は作らない");
        }

        [Test]
        public async Task Reset_RequiresSetupAgain()
        {
            var setup = await _totp.VerifyAsync("U1", "taro", null);
            await _totp.VerifyAsync("U1", "taro", CurrentCode(setup.Secret!));
            Assert.That((await _totp.VerifyAsync("U1", "taro", null)).Status, Is.EqualTo(TotpLoginStatus.CodeRequired));

            await _totp.ResetAsync("U1");
            Assert.That(await _totp.FindAsync("U1"), Is.Null);
            var again = await _totp.VerifyAsync("U1", "taro", null);
            Assert.That(again.Status, Is.EqualTo(TotpLoginStatus.Setup));
            Assert.That(again.Secret, Is.Not.EqualTo(setup.Secret), "新しい鍵");
        }

        [Test]
        public async Task UsersAreIndependent()
        {
            var a = await _totp.VerifyAsync("A", "a", null);
            var b = await _totp.VerifyAsync("B", "b", null);
            Assert.That(a.Secret, Is.Not.EqualTo(b.Secret));
            Assert.That((await _totp.VerifyAsync("A", "a", CurrentCode(b.Secret!))).Status, Is.EqualTo(TotpLoginStatus.InvalidCode), "他人の鍵のコードは通らない");
            Assert.That((await _totp.VerifyAsync("A", "a", CurrentCode(a.Secret!))).Status, Is.EqualTo(TotpLoginStatus.Ok));
        }
    }
}
