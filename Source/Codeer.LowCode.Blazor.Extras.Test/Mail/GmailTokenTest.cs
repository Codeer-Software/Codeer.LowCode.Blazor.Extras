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

        //製品のデータ層 (システム内部読み取り) のフェイク。生SQLは使わない
        class FakeSystemReader
        {
            public List<ModuleData> Rows { get; } = new();
            public SearchCondition? LastCondition;

            public Task<List<ModuleData>> ReadAsync(SearchCondition condition)
            {
                LastCondition = condition;
                return Task.FromResult(Rows);
            }
        }

        //MailUserStore: 差出人アドレスでユーザーモジュールを検索してトークン列を読む (書き込み専用列を含む内部経路)
        [Test]
        public async Task FindRefreshToken_アドレスで検索して復号する()
        {
            var designData = CreateDesignData(CreateAppUserWithToken());
            var row = new ModuleData { Name = "AppUser" };
            row.Fields["GmailToken"] = new GmailTokenFieldData { RefreshToken = GmailTokenProtector.Protect(PlainToken, Key) };
            var reader = new FakeSystemReader { Rows = { row } };
            var store = new MailUserStore(designData, reader.ReadAsync, _ => { });

            var token = await store.FindRefreshTokenAsync("tanaka@example.com", Key);

            //復号して返す
            Assert.That(token, Is.EqualTo(PlainToken));
            Assert.That(reader.LastCondition!.ModuleName, Is.EqualTo("AppUser"));
            Assert.That(reader.LastCondition.SelectFields, Does.Contain("GmailToken"));
            var condition = (FieldValueMatchCondition)reader.LastCondition.Condition!;
            Assert.That(condition.SearchTargetVariable, Is.EqualTo("Email.Value"));
        }

        [Test]
        public async Task FindRefreshToken_未登録や設定不備はnullで送信は止めない()
        {
            var designData = CreateDesignData(CreateAppUserWithToken());

            //行なし → null
            var store = new MailUserStore(designData, new FakeSystemReader().ReadAsync, _ => { });
            Assert.That(await store.FindRefreshTokenAsync("nobody@example.com", Key), Is.Null);

            //CurrentUser モジュールが存在しない → null + エラーログ
            var errors = new List<string>();
            var brokenDesign = new DesignData();
            brokenDesign.AppSettings.CurrentUserModuleDesignName = "NoSuchModule";
            var broken = new MailUserStore(brokenDesign, new FakeSystemReader().ReadAsync, errors.Add);
            Assert.That(await broken.FindRefreshTokenAsync("tanaka@example.com", Key), Is.Null);
            Assert.That(errors, Has.Count.EqualTo(1));

            //GmailTokenField が置かれていない → ユーザー単位トークンを使わない (エラーでもない)
            var noTokenModule = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            noTokenModule.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            noTokenModule.Fields.Add(new MailSenderContractFieldDesign { Name = "MailSender", Email = "Email.Value" });
            var noTokenErrors = new List<string>();
            var noToken = new MailUserStore(CreateDesignData(noTokenModule),
                new FakeSystemReader().ReadAsync, noTokenErrors.Add);
            Assert.That(await noToken.FindRefreshTokenAsync("tanaka@example.com", Key), Is.Null);
            Assert.That(noTokenErrors, Is.Empty);

            //暗号化されていない列の値は使わない (エラーログを出して null = システムトークンにフォールバック)
            var plainErrors = new List<string>();
            var plainRow = new ModuleData { Name = "AppUser" };
            plainRow.Fields["GmailToken"] = new GmailTokenFieldData { RefreshToken = PlainToken };
            var plainStore = new MailUserStore(designData,
                new FakeSystemReader { Rows = { plainRow } }.ReadAsync, plainErrors.Add);
            Assert.That(await plainStore.FindRefreshTokenAsync("tanaka@example.com", Key), Is.Null);
            Assert.That(plainErrors, Has.Count.EqualTo(1));
        }

        //「自分を差出人にする」の操作ユーザー解決 (Id で CurrentUser モジュールを引く)
        [Test]
        public async Task FindCurrentUser_Idで引いてアドレスと表示名を返す()
        {
            var designData = CreateDesignData(CreateAppUserWithToken());
            var row = new ModuleData { Name = "AppUser" };
            row.Fields["Email"] = new TextFieldData { Value = "sales@example.com" };
            row.Fields["Name"] = new TextFieldData { Value = "営業 太郎" };
            var reader = new FakeSystemReader { Rows = { row } };
            var store = new MailUserStore(designData, reader.ReadAsync, _ => { });

            var user = await store.FindCurrentUserAsync("1");

            Assert.That(user!.Email, Is.EqualTo("sales@example.com"));
            Assert.That(user.DisplayName, Is.EqualTo("営業 太郎"));
            var condition = (FieldValueMatchCondition)reader.LastCondition!.Condition!;
            Assert.That(condition.SearchTargetVariable, Is.EqualTo("Id.Value"));
        }

        [Test]
        public async Task FindCurrentUser_差出人契約が無ければnullとエラーログ()
        {
            var module = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            module.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            var errors = new List<string>();
            var store = new MailUserStore(CreateDesignData(module),
                new FakeSystemReader().ReadAsync, errors.Add);

            Assert.That(await store.FindCurrentUserAsync("1"), Is.Null);
            Assert.That(errors, Has.Count.EqualTo(1));
        }
    }
}
