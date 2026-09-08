using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Markdown
{
    /// <summary>MarkdownField のランタイム (値・検証・追記・HTML 化) とデザインチェック。</summary>
    public class MarkdownFieldTest
    {
        static async Task<(Module Module, MarkdownField Field)> CreateAsync(Action<MarkdownFieldDesign>? customize = null)
        {
            var d = new DesignData();
            var mod = new ModuleDesign { Name = "Doc" };
            mod.Fields.Add(new IdFieldDesign { Name = "Id" });
            var md = new MarkdownFieldDesign { Name = "Body" };
            customize?.Invoke(md);
            mod.Fields.Add(md);
            d.AddModule(mod);
            var services = new TestServices(d);
            var module = await services.CreateModuleAsync("Doc");
            return (module, (MarkdownField)module.GetField("Body")!);
        }

        [Test]
        public async Task 値はMarkdownのまま持ちHtmlとPlainTextは派生()
        {
            var (_, field) = await CreateAsync();
            await field.SetValueAsync("# 題\n\n**強調**");
            Assert.That(field.Value, Is.EqualTo("# 題\n\n**強調**"));
            Assert.That(field.Html, Does.Contain("<h1"));
            Assert.That(field.Html, Does.Contain("<strong>強調</strong>"));
            Assert.That(field.PlainText, Is.EqualTo("題\n強調"));
        }

        [Test]
        public async Task AppendLineは末尾に行を足す()
        {
            var (_, field) = await CreateAsync();
            await field.AppendLineAsync("- 1件目");
            await field.AppendLineAsync("- 2件目");
            Assert.That(field.Value, Is.EqualTo("- 1件目\n- 2件目"));
            Assert.That(field.IsModified, Is.True);
        }

        [Test]
        public async Task 必須と最大文字数の検証()
        {
            var (_, field) = await CreateAsync(d => { d.IsRequired = true; d.MaxLength = 5; });
            Assert.That(await field.ValidateInput(), Is.False);
            Assert.That(field.IsValid, Is.False);

            await field.SetValueAsync("12345");
            Assert.That(await field.ValidateInput(), Is.True);

            await field.SetValueAsync("123456");
            Assert.That(await field.ValidateInput(), Is.False);
            Assert.That(field.ErrorText, Does.Contain("5"));
        }

        [Test]
        public void デザインチェック_DB列があれば指摘なし()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new MarkdownFieldDesign { Name = "Body", DbColumn = "DbColumn" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void デザインチェック_存在しないDB列は指摘()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new MarkdownFieldDesign { Name = "Body", DbColumn = "NoSuchColumn" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("mod", "Body", "DbColumn");
        }
    }
}
