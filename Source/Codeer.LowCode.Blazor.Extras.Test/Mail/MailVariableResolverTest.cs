using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailVariableResolverTest
    {
        static ModuleDesign CreateDesign()
        {
            var design = new ModuleDesign { Name = "Customer" };
            design.Fields.Add(new TextFieldDesign { Name = "Name" });
            design.Fields.Add(new NumberFieldDesign { Name = "Amount", Format = "#,0" });
            design.Fields.Add(new DateFieldDesign { Name = "DueDate", Format = "yyyy/MM/dd" });
            design.Fields.Add(new SelectFieldDesign { Name = "Rank" });
            design.Fields.Add(new BooleanFieldDesign { Name = "OptOut" });
            design.Fields.Add(new TextFieldDesign { Name = "Email" });
            return design;
        }

        static ModuleData CreateData()
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Name"] = new TextFieldData { Value = "田中" };
            data.Fields["Amount"] = new NumberFieldData { Value = 12345 };
            data.Fields["DueDate"] = new DateFieldData { Value = new DateOnly(2026, 8, 9) };
            data.Fields["Rank"] = new SelectFieldData { Value = "1", DisplayText = "ゴールド" };
            data.Fields["OptOut"] = new BooleanFieldData { Value = true };
            data.Fields["Email"] = new TextFieldData { Value = "a@example.com" };
            return data;
        }

        [Test]
        public void Resolve_表示文字列で解決される()
        {
            var variables = MailVariableResolver.Resolve(CreateDesign(), CreateData(),
                new[] { "Name", "Amount", "DueDate", "Rank", "Unknown" });

            Assert.That(variables["Name"], Is.EqualTo("田中"));
            Assert.That(variables["Amount"], Is.EqualTo("12,345"));           //デザインのFormat
            Assert.That(variables["DueDate"], Is.EqualTo("2026/08/09"));      //デザインのFormat
            Assert.That(variables["Rank"], Is.EqualTo("ゴールド"));            //コード値ではなく表示テキスト
            Assert.That(variables["Unknown"], Is.EqualTo(string.Empty));      //存在しないフィールドは空
        }

        [Test]
        public void GetValueText_と_GetBooleanValue()
        {
            var data = CreateData();
            Assert.That(MailVariableResolver.GetValueText(data, "Email"), Is.EqualTo("a@example.com"));
            Assert.That(MailVariableResolver.GetValueText(data, ""), Is.EqualTo(string.Empty));
            Assert.That(MailVariableResolver.GetBooleanValue(data, "OptOut"), Is.True);
            Assert.That(MailVariableResolver.GetBooleanValue(data, "Name"), Is.False); //Boolean以外はfalse
            Assert.That(MailVariableResolver.GetBooleanValue(data, ""), Is.False);
        }

        [TestCase("こんにちは {Name} 様、{Amount}円", new[] { "Name", "Amount" })]
        [TestCase("リテラル {{Name}} のみ", new string[0])]
        [TestCase("{A}{B}{A}", new[] { "A", "B" })]
        [TestCase("", new string[0])]
        public void GetVariableNames(string template, string[] expected)
            => Assert.That(MailTemplateEngine.GetVariableNames(template), Is.EqualTo(expected));

        [TestCase("Email", "Email", "")]
        [TestCase("Email.Value", "Email", "Value")]
        [TestCase("Rank.DisplayText", "Rank", "DisplayText")]
        [TestCase("Contact.Email", "Contact.Email", "")]          //末尾が既知メンバーでなければ全体がフィールドパス
        [TestCase("Contact.Email.Value", "Contact.Email", "Value")]
        [TestCase("", "", "")]
        public void ParseToken(string token, string fieldPath, string member)
            => Assert.That(MailVariableResolver.ParseToken(token), Is.EqualTo((fieldPath, member)));

        [Test]
        public void Resolve_メンバー指定で解決される()
        {
            var variables = MailVariableResolver.Resolve(CreateDesign(), CreateData(),
                new[] { "Name.Value", "Amount.Value", "Rank.Value", "Rank.DisplayText", "Name.DisplayText" });

            Assert.That(variables["Name.Value"], Is.EqualTo("田中"));
            Assert.That(variables["Amount.Value"], Is.EqualTo("12,345"));   //Valueでも外部テキスト書式(メールはテキスト媒体)
            Assert.That(variables["Rank.Value"], Is.EqualTo("1"));          //.Value明示はコード値
            Assert.That(variables["Rank.DisplayText"], Is.EqualTo("ゴールド"));
            Assert.That(variables["Name.DisplayText"], Is.EqualTo(string.Empty)); //表示テキストを持たない型
        }

        [Test]
        public void Resolve_リンクパスはデータのリンクキーで解決される()
        {
            //リスト取得データはリンク先の値を "Contact.Xxx" キーで持つ(LinkedDataIOの分配形式)
            var design = new ModuleDesign { Name = "CampaignMember" };
            design.Fields.Add(new LinkFieldDesign { Name = "Contact", SearchCondition = { ModuleName = "Person" } });

            var personDesign = new ModuleDesign { Name = "Person" };
            personDesign.Fields.Add(new TextFieldDesign { Name = "Email" });
            personDesign.Fields.Add(new DateFieldDesign { Name = "JoinedAt", Format = "yyyy/MM/dd" });

            var data = new ModuleData { Name = "CampaignMember" };
            data.Fields["Contact"] = new LinkFieldData { Value = "p1", DisplayText = "田中" };
            data.Fields["Contact.Email"] = new TextFieldData { Value = "a@example.com" };
            data.Fields["Contact.JoinedAt"] = new DateFieldData { Value = new DateOnly(2026, 8, 9) };

            ModuleDesign? FindModule(string name) => name == "Person" ? personDesign : null;

            Assert.That(MailVariableResolver.ResolveOne(design, data, "Contact"), Is.EqualTo("田中"));            //Linkは表示テキスト
            Assert.That(MailVariableResolver.ResolveOne(design, data, "Contact.Value"), Is.EqualTo("p1"));        //.Value明示はキー値
            Assert.That(MailVariableResolver.ResolveOne(design, data, "Contact.Email"), Is.EqualTo("a@example.com"));
            Assert.That(MailVariableResolver.ResolveOne(design, data, "Contact.Email.Value"), Is.EqualTo("a@example.com"));
            Assert.That(MailVariableResolver.ResolveOne(design, data, "Contact.JoinedAt.Value", FindModule),
                Is.EqualTo("2026/08/09")); //リンク先デザインを辿って書式整形
            Assert.That(MailVariableResolver.ResolveOne(design, data, "Contact.Unknown"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetValueText_はValue付き表記とリンクパスを受ける()
        {
            var data = CreateData();
            Assert.That(MailVariableResolver.GetValueText(data, "Email.Value"), Is.EqualTo("a@example.com"));

            var row = new ModuleData { Name = "CampaignMember" };
            row.Fields["Contact.Email"] = new TextFieldData { Value = "b@example.com" };
            row.Fields["除外"] = new BooleanFieldData { Value = true };
            Assert.That(MailVariableResolver.GetValueText(row, "Contact.Email"), Is.EqualTo("b@example.com"));
            Assert.That(MailVariableResolver.GetValueText(row, "Contact.Email.Value"), Is.EqualTo("b@example.com"));
            Assert.That(MailVariableResolver.GetBooleanValue(row, "除外.Value"), Is.True);
        }
    }
}
