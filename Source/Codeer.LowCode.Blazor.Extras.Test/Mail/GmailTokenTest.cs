using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class GmailTokenTest
    {
        const string Key = "test-encryption-key";
        const string PlainToken = """{"refresh_token":"R1"}""";

        //GmailTokenHelper: 送られてきた平文を暗号化して列に入れる (送られてこないフィールドは触らない=既存維持)
        [Test]
        public void ProtectGmailTokens_平文を暗号化して入れ替える()
        {
            var module = CreateUserModule();

            //フィールドが自分で値を送ってくる → 暗号化されて入る
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["GmailToken"] = new GmailTokenFieldData { RefreshToken = PlainToken };
            GmailTokenHelper.ProtectGmailTokens(module, data, Key);
            var stored = (data.Fields["GmailToken"] as GmailTokenFieldData)!.RefreshToken!;
            Assert.That(stored, Does.StartWith("v1:"));
            Assert.That(stored, Does.Not.Contain("refresh_token"));
            Assert.That(GmailTokenProtector.Unprotect(stored, Key), Is.EqualTo(PlainToken));

            //何度呼んでも二重暗号化しない
            GmailTokenHelper.ProtectGmailTokens(module, data, Key);
            Assert.That((data.Fields["GmailToken"] as GmailTokenFieldData)!.RefreshToken, Is.EqualTo(stored));

            //送られてこない → 何も足さない (空入力=既存トークン維持)
            var noInput = new ModuleData { Name = "AppUser" };
            GmailTokenHelper.ProtectGmailTokens(module, noInput, Key);
            Assert.That(noInput.Fields.ContainsKey("GmailToken"), Is.False);

            //空 = 登録解除。暗号化せず空のまま書く
            var cleared = new ModuleData { Name = "AppUser" };
            cleared.Fields["GmailToken"] = new GmailTokenFieldData { RefreshToken = string.Empty };
            GmailTokenHelper.ProtectGmailTokens(module, cleared, Key);
            Assert.That((cleared.Fields["GmailToken"] as GmailTokenFieldData)!.RefreshToken, Is.Empty);
        }

        //鍵が無いのに保存しようとしたら止める (平文で保存しない)
        [Test]
        public void ProtectGmailTokens_鍵未設定はエラー()
        {
            var module = CreateUserModule();
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["GmailToken"] = new GmailTokenFieldData { RefreshToken = PlainToken };
            Assert.Throws<InvalidOperationException>(() => GmailTokenHelper.ProtectGmailTokens(module, data, string.Empty));
        }

        //AES-GCM: 毎回違う暗号文・鍵違いと形式違いは復号できない
        [Test]
        public void GmailTokenProtector_暗号化と復号()
        {
            var a = GmailTokenProtector.Protect(PlainToken, Key);
            var b = GmailTokenProtector.Protect(PlainToken, Key);
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(GmailTokenProtector.Unprotect(a, Key), Is.EqualTo(PlainToken));

            Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
                () => GmailTokenProtector.Unprotect(a, "another-key"));
            Assert.Throws<InvalidOperationException>(() => GmailTokenProtector.Unprotect(PlainToken, Key));
            Assert.That(GmailTokenProtector.IsProtected(PlainToken), Is.False);
            Assert.That(GmailTokenProtector.IsProtected(a), Is.True);
        }

        static ModuleDesign CreateUserModule()
        {
            var module = new ModuleDesign { Name = "AppUser" };
            module.Fields.Add(new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            module.Fields.Add(new MailSenderContractFieldDesign
            {
                Name = "MailSender",
                Email = "Email.Value",
                DisplayName = "Name.Value",
            });
            return module;
        }

        //ユーザーモジュールはデザインの CurrentUser モジュール (appsettings では指定しない)
        static DesignData CreateDesignData(ModuleDesign userModule)
        {
            var designData = new DesignData();
            designData.AddModule(userModule);
            designData.AppSettings.CurrentUserModuleDesignName = userModule.Name;
            return designData;
        }

        static ModuleDesign CreateAppUserWithToken()
        {
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            module.Fields.Add(new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            module.Fields.Add(new MailSenderContractFieldDesign
            {
                Name = "MailSender",
                Email = "Email.Value",
                DisplayName = "Name.Value",
            });
            return module;
        }

        //SQL の組み立てだけを見るフェイク (実行はしない)。実DB での動作は MailUserLookupDbTest
        class FakeDb : IDbAccessor
        {
            public DataSourceType DataSourceType = DataSourceType.SQLite;
            public DataSource? GetDataSource(string dataSource) => new() { Name = dataSource, DataSourceType = DataSourceType };
            public Task<List<IDictionary<string, object>>> QueryAsync(string dataSourceName, string query, Dictionary<string, ParamAndRawDbTypeName> args)
                => Task.FromResult(new List<IDictionary<string, object>>());
            public void StartTransaction() { }
            public Task CommitAsync() => Task.CompletedTask;
            public System.Data.Common.DbConnection GetConnection(string dataSourceName) => throw new NotSupportedException();
            public Task<int> ExecuteAsync(string dataSourceName, string query, Dictionary<string, object?> args) => throw new NotSupportedException();
            public Task<string> InsertAsync(string dataSourceName, string query, Dictionary<string, object?> args) => throw new NotSupportedException();
            public System.Data.IDbTransaction? GetTransaction(string dataSourceName) => null;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        static GmailUserTokenStore CreateSqlStore(DesignData designData, DataSourceType type = DataSourceType.SQLite, Action<string>? logError = null)
            => new(designData, new FakeDb { DataSourceType = type }, logError ?? (_ => { }));

        //識別子の引用符とパラメータ接頭辞はデータソースの種類に合わせる (全 DB 共通で使える)
        [TestCase(DataSourceType.SQLServer, "SELECT [gmail_token] AS token_value FROM [app_users] WHERE [email] = @p0", "@p0")]
        [TestCase(DataSourceType.MySQL, "SELECT `gmail_token` AS token_value FROM `app_users` WHERE `email` = @p0", "@p0")]
        [TestCase(DataSourceType.PostgreSQL, "SELECT \"gmail_token\" AS token_value FROM \"app_users\" WHERE \"email\" = @p0", "@p0")]
        [TestCase(DataSourceType.Oracle, "SELECT \"gmail_token\" AS token_value FROM \"app_users\" WHERE \"email\" = :p0", ":p0")]
        [TestCase(DataSourceType.SQLite, "SELECT \"gmail_token\" AS token_value FROM \"app_users\" WHERE \"email\" = @p0", "@p0")]
        public void トークンSQLはDBの種類に合わせて組み立てる(DataSourceType type, string expectedSql, string parameterName)
        {
            var module = CreateAppUserWithToken();
            var store = CreateSqlStore(CreateDesignData(module), type);

            var sql = store.CreateTokenSql(module, MailContracts.Sender(module)!, module.Fields.OfType<GmailTokenFieldDesign>().Single(),
                out var parameters, "tanaka@example.com");

            Assert.That(sql, Is.EqualTo(expectedSql));
            Assert.That(parameters[parameterName], Is.EqualTo("tanaka@example.com"));
        }

        [Test]
        public void トークンSQLはリンクパスのアドレス役割を組み立てない()
        {
            //アドレス役割がリンク先 (Employee.Email) だと SQL 1 文では引けないのでエラーログ + null (送信はシステムトークンへ)
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new LinkFieldDesign { Name = "Employee", DbColumn = "employee_id", SearchCondition = new() { ModuleName = "Employee" } });
            module.Fields.Add(new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            module.Fields.Add(new MailSenderContractFieldDesign { Name = "MailSender", Email = "Employee.Email.Value" });
            var errors = new List<string>();
            var store = CreateSqlStore(CreateDesignData(module), logError: errors.Add);

            var sql = store.CreateTokenSql(module, MailContracts.Sender(module)!, module.Fields.OfType<GmailTokenFieldDesign>().Single(),
                out _, "tanaka@example.com");

            Assert.That(sql, Is.Null);
            Assert.That(errors, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task GmailTokenFieldが無ければユーザー単位トークンを使わない()
        {
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new MailSenderContractFieldDesign { Name = "MailSender", Email = "Email.Value" });
            var errors = new List<string>();
            var store = CreateSqlStore(CreateDesignData(module), logError: errors.Add);

            Assert.That(await store.FindRefreshTokenAsync("tanaka@example.com", Key), Is.Null);
            Assert.That(errors, Is.Empty);

            //CurrentUser モジュールが存在しない → null + エラーログ
            var brokenDesign = new DesignData();
            brokenDesign.AppSettings.CurrentUserModuleDesignName = "NoSuchModule";
            var broken = CreateSqlStore(brokenDesign, logError: errors.Add);
            Assert.That(await broken.FindRefreshTokenAsync("tanaka@example.com", Key), Is.Null);
            Assert.That(errors, Has.Count.EqualTo(1));
        }
    }
}
