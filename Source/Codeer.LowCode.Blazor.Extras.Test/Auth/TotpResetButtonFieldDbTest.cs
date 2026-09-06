using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// TotpResetButtonField: 解除は「3 列を空にする」データを通常の Submit で書く。
    /// 実 DB (SQLite) で、書き込み専用列が Submit で書かれること・読み込みでは来ないこと・権限 (DataWriteCondition) で拒否されることを確認する。
    /// </summary>
    public class TotpResetButtonFieldDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        string _currentUserId = "U1";

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult(_currentUserId);

        static DesignData CreateDesign(string secretColumn = "totp_secret", bool ownRowOnly = false)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            //自分の行しか書けない
            if (ownRowOnly)
            {
                user.DataWriteCondition.ModuleName = "AppUser";
                user.DataWriteCondition.Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Id.Value", Comparison = MatchComparison.Equal, Variable = "CurrentUser.Id.Value" };
            }
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(new LoginAccountContractFieldDesign { Name = "LoginAccount", LoginName = "UserName", DbColumnTotpSecret = "totp_secret", DbColumnTotpConfirmed = "totp_confirmed", DbColumnTotpLastTimestep = "totp_last_timestep" });
            user.Fields.Add(new TotpResetButtonFieldDesign { Name = "TotpReset", DbColumnTotpSecret = secretColumn, DbColumnTotpConfirmed = "totp_confirmed", DbColumnTotpLastTimestep = "totp_last_timestep" });
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"totp_reset_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, totp_secret TEXT, totp_confirmed INTEGER, totp_last_timestep INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users VALUES ('U1','taro','SECRET1',1,12345),('U2','jiro','SECRET2',1,67890)", new());
            _design = CreateDesign();
            _currentUserId = "U1";
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        static ModuleSubmitData ResetSubmit(string id)
        {
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["TotpReset"] = TotpResetButtonFieldData.Cleared();
            return new ModuleSubmitData { ModuleName = "AppUser", Id = id, Update = [data] };
        }

        async Task<(string? Secret, long Confirmed, long LastTimestep)> RowAsync(string id)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT totp_secret, totp_confirmed, totp_last_timestep FROM app_users WHERE id = '{id}'", new())).Single();
            var values = row.Values.ToList();
            return (values[0] is null or DBNull ? null : values[0].ToString(), Convert.ToInt64(values[1]), Convert.ToInt64(values[2]));
        }

        [Test]
        public async Task 解除データのSubmitで3列が空になる()
        {
            var results = await CreateIO().SubmitWithTransactionAsync([ResetSubmit("U2")]);
            Assert.That(results.Count, Is.EqualTo(1));

            Assert.That(await RowAsync("U2"), Is.EqualTo(((string?)null, 0L, 0L)));
            //他の行は触らない
            Assert.That(await RowAsync("U1"), Is.EqualTo(("SECRET1", 1L, 12345L)));

            //TotpLogin から見ても未登録
            var totp = TotpLogin.Create(_design, new TotpLoginSettings { Issuer = "Test" }, _db)!;
            Assert.That(await totp.FindAsync("U2"), Is.Null);
        }

        [Test]
        public async Task 書き込み専用列は読み込みで来ない()
        {
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "AppUser" }, 0);
            Assert.That(page.Items.Count, Is.EqualTo(2));
            foreach (var item in page.Items)
            {
                var data = item.Fields.TryGetValue("TotpReset", out var d) ? d as TotpResetButtonFieldData : null;
                Assert.That(data?.TotpSecret, Is.Null);
            }
        }

        [Test]
        public async Task 行の書込権限が無ければ解除できない()
        {
            _design = CreateDesign(ownRowOnly: true);

            //他人の行は拒否 (Submit と同じくエラー結果で返る)
            var result = await CreateIO().SubmitWithTransactionAsync([ResetSubmit("U2")]);
            Assert.That(result.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(await RowAsync("U2"), Is.EqualTo(("SECRET2", 1L, 67890L)));

            //自分の行なら通る
            result = await CreateIO().SubmitWithTransactionAsync([ResetSubmit("U1")]);
            Assert.That(result.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", result.Select(e => e.ExceptionMessage)));
            Assert.That(await RowAsync("U1"), Is.EqualTo(((string?)null, 0L, 0L)));
        }

        [Test]
        public async Task フィールドは解除要求のときだけデータを送る()
        {
            var services = new TestServices(_design);
            var module = await services.CreateModuleAsync("AppUser");
            var field = (TotpResetButtonField)module.GetField("TotpReset")!;

            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
            Assert.That(field.GetData(), Is.Null);

            //新規行では解除できない (ユーザーがまだ無い)
            Assert.That(field.CanReset, Is.False);
            Assert.That(await field.ResetAsync(), Is.False);
            Assert.That(field.IsModified, Is.False);
        }

        List<string> CheckCodes()
        {
            var field = _design.Modules.Find("AppUser")!.Fields.OfType<TotpResetButtonFieldDesign>().Single();
            return field.CheckDesign(new DesignCheckContext("AppUser", _design, Utilities.CreateDataSource()))
                .Select(e => e.Code).Where(e => e.StartsWith(nameof(TotpResetButtonFieldDesign))).ToList();
        }

        [Test]
        public void デザインチェック_契約と列が違えばエラー()
        {
            Assert.That(CheckCodes(), Is.Empty);

            _design = CreateDesign(secretColumn: "totp_confirmed");
            Assert.That(CheckCodes(), Does.Contain(DesignCheckCode.Create(typeof(TotpResetButtonFieldDesign), TotpResetButtonFieldDesign.Codes.ColumnsMismatch)));
        }

        [Test]
        public void デザインチェック_ユーザーモジュール以外はエラー()
        {
            _design.AppSettings.CurrentUserModuleDesignName = "Other";
            Assert.That(CheckCodes(), Does.Contain(DesignCheckCode.Create(typeof(TotpResetButtonFieldDesign), TotpResetButtonFieldDesign.Codes.NotOnUserModule)));
        }
    }
}
