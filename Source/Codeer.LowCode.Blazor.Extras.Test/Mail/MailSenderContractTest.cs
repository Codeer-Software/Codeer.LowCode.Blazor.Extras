using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// 差出人契約 (MailSenderContractField): 「自分を差出人にする」と GmailTokenField が使う。
    /// CurrentUser のモジュールに置いていなければデザインチェックでエラー。
    /// </summary>
    public class MailSenderContractTest
    {
        static DesignData CreateDesignData(bool withContract, bool withDisplayName = true)
        {
            var designData = new DesignData();
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            if (withContract)
            {
                user.Fields.Add(new MailSenderContractFieldDesign
                {
                    Name = "MailSender",
                    Email = "Email.Value",
                    DisplayName = withDisplayName ? "Name.Value" : string.Empty,
                });
            }
            designData.AddModule(user);
            designData.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var order = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
            order.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            order.Fields.Add(new MailFieldDesign
            {
                Name = "FormMail",
                ToVariable = "Email.Value",
                Subject = "件名",
                IsFromCurrentUser = true,
            });
            designData.AddModule(order);
            return designData;
        }

        static List<DesignCheckInfo> CheckMailField(DesignData designData)
        {
            var field = designData.Modules.Find("Order")!.Fields.OfType<MailFieldDesign>().First();
            return field.CheckDesign(new DesignCheckContext("Order", designData, new Dictionary<string, List<DbTableDefinition>>()));
        }

        [Test]
        public void 差出人契約があればエラーなし()
            => Assert.That(CheckMailField(CreateDesignData(withContract: true)), Is.Empty);

        [Test]
        public void 自分を差出人にするのに契約が無ければエラー()
        {
            var result = CheckMailField(CreateDesignData(withContract: false));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Message, Does.Contain("AppUser"));
            Assert.That(result[0].Message, Does.Contain(nameof(MailSenderContractFieldDesign)));
        }

        [Test]
        public void 自分を差出人にするがOFFなら契約は要らない()
        {
            var designData = CreateDesignData(withContract: false);
            designData.Modules.Find("Order")!.Fields.OfType<MailFieldDesign>().First().IsFromCurrentUser = false;
            Assert.That(CheckMailField(designData), Is.Empty);
        }

        [Test]
        public void 表示名は空でもエラーにならない()
            => Assert.That(CheckMailField(CreateDesignData(withContract: true, withDisplayName: false)), Is.Empty);

        [Test]
        public void GmailTokenFieldを置いたら契約が必要()
        {
            //契約なしのユーザーモジュールに GmailTokenField を置く
            var designData = CreateDesignData(withContract: false);
            var user = designData.Modules.Find("AppUser")!;
            var token = new GmailTokenFieldDesign { Name = "GmailToken", DbColumnToken = "gmail_token" };
            user.Fields.Add(token);

            var result = token.CheckDesign(new DesignCheckContext("AppUser", designData,
                new Dictionary<string, List<DbTableDefinition>>()));
            Assert.That(result.Any(e => e.Message.Contains(nameof(MailSenderContractFieldDesign))), Is.True);
        }

        [Test]
        public void 契約の必須役割が空ならエラー()
        {
            var designData = CreateDesignData(withContract: true);
            var contract = designData.Modules.Find("AppUser")!.Fields.OfType<MailSenderContractFieldDesign>().First();
            contract.Email = string.Empty;

            var result = contract.CheckDesign(new DesignCheckContext("AppUser", designData,
                new Dictionary<string, List<DbTableDefinition>>()));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Message, Does.Contain(nameof(MailSenderContractFieldDesign.Email)));
        }
    }
}
