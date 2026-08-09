using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailRecipientBuilderTest
    {
        static ModuleDesign CreateDesign()
        {
            var design = new ModuleDesign { Name = "Customer" };
            design.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            design.Fields.Add(new TextFieldDesign { Name = "Name" });
            design.Fields.Add(new TextFieldDesign { Name = "Email" });
            design.Fields.Add(new BooleanFieldDesign { Name = "OptOut" });
            return design;
        }

        static ModuleData CreateRow(string id, string name, string email, bool optOut = false)
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Email"] = new TextFieldData { Value = email };
            data.Fields["OptOut"] = new BooleanFieldData { Value = optOut };
            return data;
        }

        [Test]
        public void GetVariableNames_件名と本文の変数を重複なしで取る()
            => Assert.That(MailRecipientBuilder.GetVariableNames("{Name} 様", "こちらから: {RecordUrl} ({Name})"),
                Is.EqualTo(new[] { "Name", "RecordUrl" }));

        [Test]
        public void TryBuild_除外と宛先なしはnullでRecordUrlが入る()
        {
            var design = CreateDesign();
            var names = new[] { "Name", "RecordUrl" };

            Assert.That(MailRecipientBuilder.TryBuild(design, CreateRow("2", "鈴木", "b@example.com", optOut: true),
                "Email", "OptOut", names), Is.Null); //オプトアウト
            Assert.That(MailRecipientBuilder.TryBuild(design, CreateRow("3", "佐藤", ""),
                "Email", "OptOut", names), Is.Null); //宛先なし

            var recipient = MailRecipientBuilder.TryBuild(design, CreateRow("1", "田中", "a@example.com"),
                "Email", "OptOut", names, "https://app.example.com/", "main");
            Assert.That(recipient!.To, Is.EqualTo("a@example.com"));
            Assert.That(recipient.Variables["Name"], Is.EqualTo("田中"));
            Assert.That(recipient.Variables["RecordUrl"], Is.EqualTo("https://app.example.com/main/Customer/1"));
        }

        [Test]
        public void TryBuild_AppBaseUrl未設定ならRecordUrlは解決しない()
        {
            var recipient = MailRecipientBuilder.TryBuild(CreateDesign(), CreateRow("1", "田中", "a@example.com"),
                "Email", "OptOut", new[] { "RecordUrl" });
            Assert.That(recipient!.Variables.ContainsKey("RecordUrl"), Is.False); //未解決={Name}と同じく空文字扱い
        }

        [Test]
        public void TryBuild_リンクパスの宛先と除外で組み立てる()
        {
            //名簿(CampaignMember)方式: 宛先アドレスはリンク先("Contact.Email")、除外フラグは行の Boolean
            var design = new ModuleDesign { Name = "CampaignMember" };
            design.Fields.Add(new LinkFieldDesign { Name = "Contact", SearchCondition = { ModuleName = "Person" } });
            design.Fields.Add(new BooleanFieldDesign { Name = "除外" });

            ModuleData CreateMember(string email, bool exclude)
            {
                var row = new ModuleData { Name = "CampaignMember" };
                row.Fields["Contact"] = new LinkFieldData { Value = "p1", DisplayText = "田中" };
                row.Fields["Contact.Email"] = new TextFieldData { Value = email };
                row.Fields["除外"] = new BooleanFieldData { Value = exclude };
                return row;
            }

            var names = new[] { "Contact" };
            Assert.That(MailRecipientBuilder.TryBuild(design, CreateMember("a@example.com", exclude: true),
                "Contact.Email.Value", "除外.Value", names), Is.Null);

            var recipient = MailRecipientBuilder.TryBuild(design, CreateMember("a@example.com", exclude: false),
                "Contact.Email.Value", "除外.Value", names);
            Assert.That(recipient!.To, Is.EqualTo("a@example.com"));
            Assert.That(recipient.Variables["Contact"], Is.EqualTo("田中"));
        }
    }
}
