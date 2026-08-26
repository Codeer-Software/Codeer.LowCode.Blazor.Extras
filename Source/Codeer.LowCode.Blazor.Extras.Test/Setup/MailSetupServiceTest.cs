using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class MailSetupServiceTest : SetupTestBase
    {
        static MailSetupOptions DefaultOptions() => new()
        {
            DataSourceName = "Main",
            AddSenderContract = true,
        };

        [Test]
        public void 差出人契約と履歴モジュールが揃い契約が解決する()
        {
            CreateFixture();
            var result = MailSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[] { "MailHistory", "MailHistoryDetail" }));

            var d = Load();
            var user = d.Modules.Find("AppUser")!;
            var sender = user.Fields.OfType<MailSenderContractFieldDesign>().Single();
            Assert.That(sender.Email, Is.EqualTo("Email.Value"));
            Assert.That(sender.DisplayName, Is.EqualTo("Name.Value"));
            Assert.That(user.Fields.OfType<GmailTokenFieldDesign>().Any(), Is.False);

            var module = d.Modules.Find("MailHistory")!;
            Assert.That(module.DbTable, Is.EqualTo("mail_histories"));
            Assert.That(MailContracts.History(module), Is.Not.Null);

            //契約チェックが通ること (既定役割のフィールドが全部あること)
            var dbDefs = new Dictionary<string, List<DbTableDefinition>>();
            Assert.That(module.Fields.OfType<MailHistoryContractFieldDesign>().Single()
                .CheckDesign(new DesignCheckContext("MailHistory", d, dbDefs)), Is.Empty);
            Assert.That(sender.CheckDesign(new DesignCheckContext("AppUser", d, dbDefs)), Is.Empty);

            //履歴はシステムの記録 = 画面からは誰も書けない
            var protect = (FieldValueMatchCondition)module.UserWriteCondition.Condition!;
            Assert.That(protect.SearchTargetVariable, Is.EqualTo("Id.Value"));

            //PageFrame リンクと appsettings 案内 (Mail セクション。インフラの選択はアプリ側)
            Assert.That(d.PageFrames.Find("Main")!.Left.Links.Select(e => e.Module), Does.Contain("MailHistory"));
            var notes = string.Join("\n", result.Notes);
            Assert.That(notes, Does.Contain("\"HistoryModuleName\": \"MailHistory\"").And.Contain("DefaultInfraName").And.Not.Contain("TokenEncryptionKey"));

            Assert.That(string.Join("\n", result.Ddl), Does.Contain("CREATE TABLE mail_histories").And.Contain("CREATE TABLE mail_history_details"));

            //明細: 履歴契約の Details → 一覧 → 明細モジュール (明細契約を実装)。契約チェックが通る
            var contract = module.Fields.OfType<MailHistoryContractFieldDesign>().Single();
            Assert.That(contract.Details, Is.EqualTo("Details"));
            var detail = d.Modules.Find("MailHistoryDetail")!;
            Assert.That(MailContracts.HistoryDetail(detail), Is.Not.Null);
            Assert.That(detail.Fields.OfType<MailHistoryDetailContractFieldDesign>().Single()
                .CheckDesign(new DesignCheckContext("MailHistoryDetail", d, dbDefs)), Is.Empty);
            Assert.That(notes, Does.Contain("MailHistoryDetail").And.Contain("閲覧権限"));
        }

        [Test]
        public void 明細なしを選ぶと履歴だけ生成され契約のDetailsは空()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.CreateHistoryDetailModule = false;
            var result = MailSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[] { "MailHistory" }));
            var module = Load().Modules.Find("MailHistory")!;
            Assert.That(module.Fields.OfType<MailHistoryContractFieldDesign>().Single().Details, Is.Empty);
            Assert.That(module.Fields.Any(e => e.Name == "Details"), Is.False);
        }

        [Test]
        public void Gmailトークン欄を追加すると列DDLと鍵の案内が出る()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.AddGmailTokenField = true;
            var result = MailSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var user = Load().Modules.Find("AppUser")!;
            var token = user.Fields.OfType<GmailTokenFieldDesign>().Single();
            Assert.That(token.DbColumnToken, Is.EqualTo("gmail_token"));
            Assert.That(string.Join("\n", result.Ddl), Does.Contain("gmail_token"));
            Assert.That(string.Join("\n", result.Notes), Does.Contain("TokenEncryptionKey"));
        }

        [Test]
        public void 既定は差出人契約なしで履歴の保護条件はデザインのCurrentUserモジュール()
        {
            CreateFixture();
            var options = new MailSetupOptions { DataSourceName = "Main", UserModuleName = "Nobody" };
            var result = MailSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var d = Load();
            Assert.That(d.Modules.Find("AppUser")!.Fields.OfType<MailSenderContractFieldDesign>().Any(), Is.False);
            Assert.That(d.Modules.Find("MailHistory")!.UserWriteCondition.ModuleName, Is.EqualTo("AppUser"));
        }

        [Test]
        public void 履歴なし差出人契約なしでも案内だけ出る()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.AddSenderContract = false;
            options.CreateHistoryModule = false;
            var before = ReadModuleJson("AppUser");
            var result = MailSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.Empty);
            Assert.That(ReadModuleJson("AppUser"), Is.EqualTo(before));
            Assert.That(string.Join("\n", result.Notes), Does.Contain("DefaultInfraName").And.Not.Contain("HistoryModuleName"));
        }

        [Test]
        public void 冪等で二回目は何も作らない()
        {
            CreateFixture();
            MailSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);
            var history = ReadModuleJson("MailHistory");
            var user = ReadModuleJson("AppUser");

            var second = MailSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(second.CreatedModules, Is.Empty);
            Assert.That(second.SkippedModules, Is.EquivalentTo(new[] { "MailHistory" }));
            Assert.That(ReadModuleJson("MailHistory"), Is.EqualTo(history));
            Assert.That(ReadModuleJson("AppUser"), Is.EqualTo(user));
            Assert.That(Load().Modules.Find("AppUser")!.Fields.OfType<MailSenderContractFieldDesign>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void メールアドレスフィールドが無ければ差出人契約は追加せず案内する()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.UserEmailField = "NoSuchField";
            options.CreateHistoryModule = false;
            var result = MailSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(Load().Modules.Find("AppUser")!.Fields.OfType<MailSenderContractFieldDesign>().Any(), Is.False);
            Assert.That(string.Join("\n", result.Notes), Does.Contain("NoSuchField"));
        }
    }
}
