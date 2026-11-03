using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// SessionRevokeButtonField: 全端末ログアウトは「セッションの印を NULL にする」データを通常の Submit で書く。
    /// 実 DB (SQLite) で、書き込み専用列が Submit で書かれること・読み込みでは来ないこと・権限 (DataWriteCondition) で拒否されること・
    /// NULL にした後は照合 (LoginAccountStore.IsSessionValidAsync) で落ち、次のログインで新しい印が採番されることを確認する。
    /// </summary>
    public class SessionRevokeButtonFieldDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        string _currentUserId = "U1";

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult(_currentUserId);

        static DesignData CreateDesign(string buttonColumn = "session_stamp", string contractColumn = "session_stamp", bool ownRowOnly = false)
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
            user.Fields.Add(new PasswordFieldDesign { Name = "Password" });
            user.Fields.Add(new LoginAccountContractFieldDesign
            {
                Name = "LoginAccount", LoginName = "UserName", PasswordField = "Password", DbColumnPasswordHash = "hash", DbColumnPasswordSalt = "salt", DbColumnSessionStamp = contractColumn,
            });
            user.Fields.Add(new SessionRevokeButtonFieldDesign { Name = "SessionRevoke", DbColumnSessionStamp = buttonColumn });
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"session_revoke_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, hash TEXT, salt TEXT, session_stamp TEXT)", new());
            var hashed = PasswordHashHelper.CreateHash("p@ss");
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users VALUES ('U1','taro',@p1,@p2,'STAMP1'),('U2','jiro',@p1,@p2,'STAMP2')", new() { { "@p1", hashed.Hash }, { "@p2", hashed.Salt } });
            _design = CreateDesign();
            _currentUserId = "U1";
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        static ModuleSubmitData RevokeSubmit(string id)
        {
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["SessionRevoke"] = SessionRevokeButtonFieldData.Cleared();
            return new ModuleSubmitData { ModuleName = "AppUser", Id = id, Update = [data] };
        }

        async Task<(string? UserName, string? Stamp)> RowAsync(string id)
        {
            var row = (await _db.QueryAsync(Ds, $"SELECT user_name, session_stamp FROM app_users WHERE id = '{id}'", new())).Single();
            var values = row.Values.ToList();
            return (values[0]?.ToString(), values[1] is null or DBNull ? null : values[1].ToString());
        }

        [Test]
        public async Task ログアウトデータのSubmitで印がNULLになる()
        {
            var results = await CreateIO().SubmitWithTransactionAsync([RevokeSubmit("U2")]);
            Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

            Assert.That(await RowAsync("U2"), Is.EqualTo(("jiro", (string?)null)));
            //他の行は触らない
            Assert.That(await RowAsync("U1"), Is.EqualTo(("taro", "STAMP1")));
        }

        //ボタンのデータに載った印の値はクライアントに決めさせない (何が来ても NULL)
        [Test]
        public async Task 送られた印の値は使わずNULLにする()
        {
            var submit = RevokeSubmit("U2");
            submit.Update[0].Fields["SessionRevoke"] = new SessionRevokeButtonFieldData { SessionStamp = "attacker" };
            var io = new PasswordHashingModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            var results = await io.SubmitWithTransactionAsync([submit]);
            Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join(Environment.NewLine, results.Select(e => e.ExceptionMessage)));
            Assert.That(await RowAsync("U2"), Is.EqualTo(("jiro", (string?)null)));
        }

        [Test]
        public async Task 印をNULLにすると照合で落ち次のログインで新しい印になる()
        {
            var accounts = LoginAccountStore.Create(_design, _db)!;
            Assert.That(await accounts.IsSessionValidAsync("U2", "STAMP2"), Is.True);

            await CreateIO().SubmitWithTransactionAsync([RevokeSubmit("U2")]);
            Assert.That(await accounts.IsSessionValidAsync("U2", "STAMP2"), Is.False, "発行済みの Cookie は無効");
            Assert.That(await accounts.IsSessionValidAsync("U1", "STAMP1"), Is.True, "他のユーザーはそのまま");

            var login = await accounts.VerifyPasswordAsync("jiro", "p@ss");
            Assert.That(login!.SessionStamp, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("STAMP2"));
            Assert.That(await accounts.IsSessionValidAsync("U2", login.SessionStamp), Is.True);
        }

        //パスワード変更と同じ保存: 契約が新しい印を書くのでボタンのデータは外れる (同じ列を SET に 2 回並べない)。新しい印でも全端末は失効する
        [Test]
        public async Task パスワード変更と同じ保存なら新しい印になる()
        {
            var before = (await _db.QueryAsync(Ds, "SELECT hash, salt FROM app_users WHERE id = 'U2'", new())).Single().Values.Select(e => e?.ToString()).ToList();
            var submit = RevokeSubmit("U2");
            submit.Update[0].Fields["Password"] = new PasswordFieldData { Value = "changed" };
            var io = new PasswordHashingModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            var results = await io.SubmitWithTransactionAsync([submit]);
            Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join(Environment.NewLine, results.Select(e => e.ExceptionMessage)));

            var stamp = (await RowAsync("U2")).Stamp;
            Assert.That(stamp, Is.Not.Null.And.Not.Empty.And.Not.EqualTo("STAMP2"), "契約の新しい印");
            var after = (await _db.QueryAsync(Ds, "SELECT hash, salt FROM app_users WHERE id = 'U2'", new())).Single().Values.Select(e => e?.ToString()).ToList();
            Assert.That(after[0], Is.Not.Empty.And.Not.EqualTo(before[0]));
            Assert.That(after[1], Is.Not.Empty.And.Not.EqualTo(before[1]));

            var accounts = LoginAccountStore.Create(_design, _db)!;
            Assert.That(await accounts.IsSessionValidAsync("U2", "STAMP2"), Is.False);
            Assert.That((await accounts.VerifyPasswordAsync("jiro", "changed"))!.SessionStamp, Is.EqualTo(stamp));
        }

        [Test]
        public async Task 書き込み専用列は読み込みで来ない()
        {
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "AppUser" }, 0);
            Assert.That(page.Items.Count, Is.EqualTo(2));
            foreach (var item in page.Items)
            {
                var data = item.Fields.TryGetValue("SessionRevoke", out var d) ? d as SessionRevokeButtonFieldData : null;
                Assert.That(data?.SessionStamp, Is.Null);
            }
        }

        [Test]
        public async Task 行の書込権限が無ければログアウトさせられない()
        {
            _design = CreateDesign(ownRowOnly: true);

            //他人の行は拒否 (Submit と同じくエラー結果で返る)
            var result = await CreateIO().SubmitWithTransactionAsync([RevokeSubmit("U2")]);
            Assert.That(result.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(await RowAsync("U2"), Is.EqualTo(("jiro", "STAMP2")));

            //自分の行なら通る
            result = await CreateIO().SubmitWithTransactionAsync([RevokeSubmit("U1")]);
            Assert.That(result.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", result.Select(e => e.ExceptionMessage)));
            Assert.That(await RowAsync("U1"), Is.EqualTo(("taro", (string?)null)));
        }

        [Test]
        public async Task フィールドはログアウト要求のときだけデータを送る()
        {
            var services = new TestServices(_design);
            var module = await services.CreateModuleAsync("AppUser");
            var field = (SessionRevokeButtonField)module.GetField("SessionRevoke")!;

            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetSubmitData().FieldData, Is.Null);
            Assert.That(field.GetData(), Is.Null);

            //新規行ではログアウトさせられない (ユーザーがまだ無い)
            Assert.That(field.CanRevoke, Is.False);
            Assert.That(await field.RevokeAsync(), Is.False);
            Assert.That(field.IsModified, Is.False);
        }

        List<string> CheckCodes()
        {
            var field = _design.Modules.Find("AppUser")!.Fields.OfType<SessionRevokeButtonFieldDesign>().Single();
            return field.CheckDesign(new DesignCheckContext("AppUser", _design, Utilities.CreateDataSource()))
                .Select(e => e.Code).Where(e => e.StartsWith(nameof(SessionRevokeButtonFieldDesign))).ToList();
        }

        static string Code(int number) => DesignCheckCode.Create(typeof(SessionRevokeButtonFieldDesign), number);

        [Test]
        public void デザインチェック_正しい設計は指摘なし()
        {
            Assert.That(CheckCodes(), Is.Empty);
        }

        [Test]
        public void デザインチェック_契約と列が違えばエラー()
        {
            _design = CreateDesign(buttonColumn: "user_name");
            Assert.That(CheckCodes(), Does.Contain(Code(2 /* ColumnsMismatch */)));

            //契約に印の列が無い
            _design = CreateDesign(contractColumn: "");
            Assert.That(CheckCodes(), Does.Contain(Code(2)));

            //契約が無い
            _design = CreateDesign();
            var user = _design.Modules.Find("AppUser")!;
            user.Fields.RemoveAll(e => e is LoginAccountContractFieldDesign);
            Assert.That(CheckCodes(), Does.Contain(Code(2)));
        }

        [Test]
        public void デザインチェック_ユーザーモジュール以外はエラー()
        {
            _design.AppSettings.CurrentUserModuleDesignName = "Other";
            Assert.That(CheckCodes(), Does.Contain(Code(1 /* NotOnUserModule */)));
        }
    }
}
