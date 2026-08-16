using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class MailHistorySetupServiceTest : SetupTestBase
    {
        static MailHistorySetupOptions DefaultOptions() => new()
        {
            DataSourceName = "Main",
        };

        [Test]
        public void 履歴モジュールが生成され契約が解決する()
        {
            CreateFixture();
            var result = MailHistorySetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[] { "MailHistory" }));

            var d = Load();
            var module = d.Modules.Find("MailHistory")!;
            Assert.That(module, Is.Not.Null);
            Assert.That(module.DbTable, Is.EqualTo("mail_histories"));
            Assert.That(MailContracts.History(module), Is.Not.Null);

            //契約チェックが通ること (既定役割のフィールドが全部あること)
            var dbDefs = new Dictionary<string, List<DbTableDefinition>>();
            Assert.That(module.Fields.OfType<MailHistoryContractFieldDesign>().Single()
                .CheckDesign(new DesignCheckContext("MailHistory", d, dbDefs)), Is.Empty);

            //履歴はシステムの記録 = 画面からは誰も書けない
            var protect = (FieldValueMatchCondition)module.UserWriteCondition.Condition!;
            Assert.That(protect.SearchTargetVariable, Is.EqualTo("Id.Value"));

            //PageFrame リンクと appsettings 案内
            Assert.That(d.PageFrames.Find("Main")!.Left.Links.Select(e => e.Module), Does.Contain("MailHistory"));
            Assert.That(string.Join("\n", result.Notes), Does.Contain("HistoryModuleName"));

            Assert.That(string.Join("\n", result.Ddl), Does.Contain("CREATE TABLE mail_histories"));
        }

        [Test]
        public void 冪等で二回目は生成されない()
        {
            CreateFixture();
            MailHistorySetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);
            var before = ReadModuleJson("MailHistory");

            var second = MailHistorySetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(second.CreatedModules, Is.Empty);
            Assert.That(second.SkippedModules, Is.EquivalentTo(new[] { "MailHistory" }));
            Assert.That(ReadModuleJson("MailHistory"), Is.EqualTo(before));
        }
    }
}
