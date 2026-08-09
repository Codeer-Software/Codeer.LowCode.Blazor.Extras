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
    }
}
