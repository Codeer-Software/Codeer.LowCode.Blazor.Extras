using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// ユーザーモジュールの読み取り 2 種を実DB (SQLite) で検証する。
    /// 操作ユーザー行 (MailCurrentUserResolver) は製品のデータ層 (ModuleDataIO)、
    /// 書き込み専用の Gmail トークン列 (GmailUserTokenStore) はデザインから組み立てた SQL (IDbAccessor) で読む。
    /// テンプレートと同じく DataService の部品 (ModuleDataIO / DbAccess) を渡すだけで動くこと。
    /// </summary>
    public class MailUserLookupDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        const string Key = "test-encryption-key";
        const string PlainToken = """{"refresh_token":"R1"}""";

        DbAccessor _db = null!;
        string _dbFile = null!;
        DesignData _designData = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"mail_user_store_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, name TEXT, email TEXT, gmail_token TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users VALUES ('1','営業 太郎','sales@example.com',@p0),('2','平文 次郎','plain@example.com',@p1),('3','未登録 三郎','none@example.com',NULL)",
                new() { ["@p0"] = GmailTokenProtector.Protect(PlainToken, Key), ["@p1"] = PlainToken });
            _designData = CreateDesignData();
            _errors.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_dbFile); } catch { }
        }

        static DesignData CreateDesignData()
        {
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            module.Fields.Add(new MailSenderContractFieldDesign { Name = "MailSender", Email = "Email.Value", DisplayName = "Name.Value" });
            var d = new DesignData();
            d.AddModule(module);
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            return d;
        }

        ModuleDataIO CreateIO() => new(_designData, this, _db, new TemporaryFileManager(_db, [], []));
        GmailUserTokenStore CreateTokenStore() => new(_designData, _db, new GmailSettings { TokenEncryptionKey = Key }, _errors.Add);
        MailCurrentUserResolver CreateResolver() => new(_designData, CreateIO(), _errors.Add);

        [Test]
        public async Task トークン列は製品のデータ層では読めず_SQL経路で復号して読める()
        {
            //書き込み専用列 (IsWriteOnly) は製品のデータ層の SELECT に含まれない = クライアントに返らない
            var rows = (await CreateIO().GetListAsync(new SearchCondition { ModuleName = "AppUser", SelectFields = ["Id", "GmailToken"] }, 0)).Items;
            Assert.That(rows, Has.Count.EqualTo(3));
            Assert.That(rows.All(r => !r.Fields.ContainsKey("GmailToken")), Is.True);

            //GmailUserTokenStore はデザインから組み立てた SQL で読んで復号する
            var token = await CreateTokenStore().FindRefreshTokenAsync("sales@example.com");
            Assert.That(token, Is.EqualTo(PlainToken));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task トークン未登録や平文はnullで送信は止めない()
        {
            var store = CreateTokenStore();

            //未登録 (NULL) / 該当アドレスなし → null (システムトークンにフォールバック)
            Assert.That(await store.FindRefreshTokenAsync("none@example.com"), Is.Null);
            Assert.That(await store.FindRefreshTokenAsync("nobody@example.com"), Is.Null);
            Assert.That(_errors, Is.Empty);

            //暗号化されていない値は使わない (エラーログ + null)
            Assert.That(await store.FindRefreshTokenAsync("plain@example.com"), Is.Null);
            Assert.That(_errors, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task 操作ユーザーは製品のデータ層で引いてアドレスと表示名を返す()
        {
            var user = await CreateResolver().FindCurrentUserAsync("1");

            Assert.That(user!.Email, Is.EqualTo("sales@example.com"));
            Assert.That(user.DisplayName, Is.EqualTo("営業 太郎"));
            Assert.That(await CreateResolver().FindCurrentUserAsync("999"), Is.Null);
        }
    }
}
