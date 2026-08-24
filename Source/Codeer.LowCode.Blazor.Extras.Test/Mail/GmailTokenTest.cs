using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Data;
using System.Data.Common;

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
            return module;
        }

        //MailUserStore: 差出人アドレスでユーザーモジュールを検索してトークン列を読む (書き込み専用列なので生SQL)
        [Test]
        public async Task FindRefreshToken_設計からテーブルと列を解決してSQLで引き復号する()
        {
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            var designData = new DesignData();
            designData.AddModule(module);

            var db = new FakeDbAccessor { Rows = { new Dictionary<string, object> { ["gmail_token"] = GmailTokenProtector.Protect(PlainToken, Key) } } };
            var store = new MailUserStore(designData, new MailConfig
            {
                UserModuleName = "AppUser",
                UserEmailFieldName = "Email",
                TokenEncryptionKey = Key,
            }, db, _ => { });

            var token = await store.FindRefreshTokenAsync("tanaka@example.com", "GmailToken");

            //復号して返す
            Assert.That(token, Is.EqualTo(PlainToken));
            Assert.That(db.LastDataSource, Is.EqualTo("Main"));
            Assert.That(db.LastQuery, Is.EqualTo("select gmail_token from app_users where email = @mailAddress"));
            Assert.That(db.LastArgs!["mailAddress"].Value, Is.EqualTo("tanaka@example.com"));
        }

        [Test]
        public async Task FindRefreshToken_未登録や設定不備はnullで送信は止めない()
        {
            var designData = new DesignData();
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            designData.AddModule(module);

            //行なし → null
            var store = new MailUserStore(designData, new MailConfig
            {
                UserModuleName = "AppUser",
                UserEmailFieldName = "Email",
            }, new FakeDbAccessor(), _ => { });
            Assert.That(await store.FindRefreshTokenAsync("nobody@example.com", "GmailToken"), Is.Null);

            //モジュール不在 → null + エラーログ
            var errors = new List<string>();
            var broken = new MailUserStore(designData, new MailConfig
            {
                UserModuleName = "NoSuchModule",
                UserEmailFieldName = "Email",
            }, new FakeDbAccessor(), errors.Add);
            Assert.That(await broken.FindRefreshTokenAsync("tanaka@example.com", "GmailToken"), Is.Null);
            Assert.That(errors, Has.Count.EqualTo(1));

            //暗号化されていない列の値は使わない (エラーログを出して null = システムトークンにフォールバック)
            var plainErrors = new List<string>();
            var plainDb = new FakeDbAccessor { Rows = { new Dictionary<string, object> { ["gmail_token"] = PlainToken } } };
            var plainStore = new MailUserStore(designData, new MailConfig
            {
                UserModuleName = "AppUser",
                UserEmailFieldName = "Email",
                TokenEncryptionKey = Key,
            }, plainDb, plainErrors.Add);
            Assert.That(await plainStore.FindRefreshTokenAsync("tanaka@example.com", "GmailToken"), Is.Null);
            Assert.That(plainErrors, Has.Count.EqualTo(1));
        }

        class FakeDbAccessor : IDbAccessor
        {
            public List<IDictionary<string, object>> Rows { get; } = new();
            public string? LastDataSource;
            public string? LastQuery;
            public Dictionary<string, ParamAndRawDbTypeName>? LastArgs;

            public Task<List<IDictionary<string, object>>> QueryAsync(string dataSourceName, string query, Dictionary<string, ParamAndRawDbTypeName> args)
            {
                LastDataSource = dataSourceName;
                LastQuery = query;
                LastArgs = args;
                return Task.FromResult(Rows);
            }

            public DataSource? GetDataSource(string dataSource) => null;
            public void StartTransaction() { }
            public Task CommitAsync() => Task.CompletedTask;
            public DbConnection GetConnection(string dataSourceName) => throw new NotImplementedException();
            public Task<int> ExecuteAsync(string dataSourceName, string query, Dictionary<string, object?> args) => throw new NotImplementedException();
            public Task<string> InsertAsync(string dataSourceName, string query, Dictionary<string, object?> args) => throw new NotImplementedException();
            public IDbTransaction? GetTransaction(string dataSourceName) => null;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
