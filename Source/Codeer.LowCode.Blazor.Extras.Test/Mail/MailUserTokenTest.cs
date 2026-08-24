using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Data;
using System.Data.Common;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailUserTokenTest
    {
        //MailUserTokenHelper: 入力フィールドに値があるときだけ書き込み専用列を更新する (PasswordHash と同じ流儀)
        [Test]
        public void ApplyMailToken_入力があるときだけトークンを書き込む()
        {
            var module = new ModuleDesign { Name = "AppUser" };
            module.Fields.Add(new PasswordFieldDesign { Name = "TokenInput" });
            module.Fields.Add(new MailTokenFieldDesign { Name = "GmailToken", TokenInputFieldName = "TokenInput", DbColumnToken = "gmail_token" });

            //入力あり → トークン列に書かれる
            var data = new ModuleData { Name = "AppUser" };
            data.Fields["TokenInput"] = new PasswordFieldData { Value = """{"refresh_token":"R1"}""" };
            MailUserTokenHelper.ApplyMailToken(module, data);
            Assert.That((data.Fields["GmailToken"] as MailTokenFieldData)!.Token, Is.EqualTo("""{"refresh_token":"R1"}"""));

            //入力なし → 既存維持 (何も書かない)
            var noInput = new ModuleData { Name = "AppUser" };
            noInput.Fields["TokenInput"] = new PasswordFieldData { Value = "" };
            MailUserTokenHelper.ApplyMailToken(module, noInput);
            Assert.That(noInput.Fields.ContainsKey("GmailToken"), Is.False);
        }

        //MailUserTokenStore: 差出人アドレスでユーザーモジュールを検索してトークン列を読む (書き込み専用列なので生SQL)
        [Test]
        public async Task FindRefreshToken_設計からテーブルと列を解決してSQLで引く()
        {
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            module.Fields.Add(new MailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
            var designData = new DesignData();
            designData.AddModule(module);

            var db = new FakeDbAccessor { Rows = { new Dictionary<string, object> { ["gmail_token"] = """{"refresh_token":"R1"}""" } } };
            var store = new MailUserStore(designData, new MailConfig
            {
                UserModuleName = "AppUser",
                UserEmailFieldName = "Email",
            }, db, _ => { });

            var token = await store.FindRefreshTokenAsync("tanaka@example.com", "GmailToken");

            Assert.That(token, Is.EqualTo("""{"refresh_token":"R1"}"""));
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
            module.Fields.Add(new MailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" });
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
