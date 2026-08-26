using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// 一斉送信の宛先契約 (BulkMailRecipientContractField): 宛先の指定は行モジュール側の契約が持ち、
    /// BulkMailField は「宛先リストの先が契約を実装しているか」だけを見る。
    /// </summary>
    public class BulkMailRecipientContractTest
    {
        //MailCampaign --(Members: CampaignMember の一覧)--> CampaignMember --(Link Contact)--> Contact
        static DesignData CreateDesignData(bool withContract = true, bool withOptOut = true)
        {
            var designData = new DesignData();

            var contact = new ModuleDesign { Name = "Contact", DataSourceName = "Main", DbTable = "contacts" };
            contact.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            contact.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            contact.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            contact.Fields.Add(new BooleanFieldDesign { Name = "OptOut", DbColumn = "opt_out" });
            designData.AddModule(contact);

            var member = new ModuleDesign { Name = "CampaignMember", DataSourceName = "Main", DbTable = "campaign_members" };
            member.Fields.Add(new LinkFieldDesign
            {
                Name = "Contact",
                DbColumn = "contact_id",
                ValueVariable = "Id.Value",
                DisplayTextVariable = "Name.Value",
                SearchCondition = new() { ModuleName = "Contact" },
            });
            if (withContract)
            {
                member.Fields.Add(new BulkMailRecipientContractFieldDesign
                {
                    Name = "MailRecipient",
                    Email = "Contact.Email.Value",
                    OptOut = withOptOut ? "Contact.OptOut.Value" : string.Empty,
                });
            }
            designData.AddModule(member);

            var campaign = new ModuleDesign { Name = "MailCampaign", DataSourceName = "Main", DbTable = "mail_campaigns" };
            campaign.Fields.Add(new TextFieldDesign { Name = "Subject", DbColumn = "subject" });
            campaign.Fields.Add(new ListFieldDesign
            {
                Name = "Members",
                SearchCondition = new() { ModuleName = "CampaignMember" },
            });
            campaign.Fields.Add(new BulkMailFieldDesign
            {
                Name = "BulkMail",
                RecipientListFieldName = "Members",
                SubjectVariable = "Subject.Value",
                Body = "本文",
            });
            designData.AddModule(campaign);
            return designData;
        }

        static List<DesignCheckInfo> CheckBulkMail(DesignData designData)
        {
            var campaign = designData.Modules.Find("MailCampaign")!;
            var field = campaign.Fields.OfType<BulkMailFieldDesign>().First();
            return field.CheckDesign(new DesignCheckContext("MailCampaign", designData, new Dictionary<string, List<DbTableDefinition>>()));
        }

        [Test]
        public void 宛先リストの行モジュールが契約を実装していればエラーなし()
            => Assert.That(CheckBulkMail(CreateDesignData()), Is.Empty);

        [Test]
        public void 契約が無ければデザインチェックエラー()
        {
            var result = CheckBulkMail(CreateDesignData(withContract: false));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Message, Does.Contain("CampaignMember"));
            Assert.That(result[0].Message, Does.Contain(nameof(BulkMailRecipientContractFieldDesign)));
        }

        [Test]
        public void 配信停止は任意_空でもエラーにならない()
            => Assert.That(CheckBulkMail(CreateDesignData(withOptOut: false)), Is.Empty);

        [Test]
        public void 役割の変数が解決できなければ契約側でエラー()
        {
            var designData = CreateDesignData();
            var member = designData.Modules.Find("CampaignMember")!;
            var contract = member.Fields.OfType<BulkMailRecipientContractFieldDesign>().First();
            contract.Email = "Contact.NoSuchField.Value";

            var result = contract.CheckDesign(new DesignCheckContext("CampaignMember", designData, new Dictionary<string, List<DbTableDefinition>>()));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Message, Does.Contain("NoSuchField"));
        }

        [Test]
        public void 同じ契約を2つ置いたらエラー()
        {
            var designData = CreateDesignData();
            var member = designData.Modules.Find("CampaignMember")!;
            member.Fields.Add(new BulkMailRecipientContractFieldDesign { Name = "MailRecipient2", Email = "Contact.Email.Value" });
            var contract = member.Fields.OfType<BulkMailRecipientContractFieldDesign>().First();

            var result = contract.CheckDesign(new DesignCheckContext("CampaignMember", designData, new Dictionary<string, List<DbTableDefinition>>()));
            Assert.That(result, Is.Not.Empty);
        }

        //役割の値はリンク越しのリネームにも追従する (コアの AddVariable がリンクパスを見る)
        [Test]
        public void リンク先のフィールド改名に役割が追従する()
        {
            var designData = CreateDesignData();
            var contract = designData.Modules.Find("CampaignMember")!.Fields.OfType<BulkMailRecipientContractFieldDesign>().First();
            var context = new RenameContext(designData)
            {
                ModuleName = "Contact",   //改名されるフィールドのモジュール
                OwnerModule = "CampaignMember",
                Source = "Email",
                Destination = "MailAddress",
                Type = RenameType.Field,
            };

            var result = contract.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(contract.Email, Is.EqualTo("Contact.MailAddress.Value"));
        }

        [Test]
        public void リンクフィールド自身の改名にも追従する()
        {
            var designData = CreateDesignData();
            var contract = designData.Modules.Find("CampaignMember")!.Fields.OfType<BulkMailRecipientContractFieldDesign>().First();
            var context = new RenameContext(designData)
            {
                ModuleName = "CampaignMember",
                OwnerModule = "CampaignMember",
                Source = "Contact",
                Destination = "Person",
                Type = RenameType.Field,
            };

            var result = contract.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(contract.Email, Is.EqualTo("Person.Email.Value"));
            Assert.That(contract.OptOut, Is.EqualTo("Person.OptOut.Value"));
        }
    }
}
