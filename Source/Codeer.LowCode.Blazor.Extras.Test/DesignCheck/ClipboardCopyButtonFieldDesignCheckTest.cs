using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class ClipboardCopyButtonFieldDesignCheckTest
    {
        const string SourceInvalidMessage =
            "コピー元フィールドとコピーする文字列は、どちらか一方だけを設定してください。";

        static (DesignData, ModuleDesign) CreateDesignDataWithSources()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new MarkupStringFieldDesign { Name = "Prompt", RawHtml = "<code>hello</code>" });
            module.Fields.Add(new LabelFieldDesign { Name = "Caption", Text = "caption" });
            module.Fields.Add(new ButtonFieldDesign { Name = "OtherButton" });
            return (designData, module);
        }

        static List<DesignCheckInfo> Check(DesignData designData, ModuleDesign module, ClipboardCopyButtonFieldDesign field)
        {
            module.Fields.Add(field);
            return field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
        }

        [Test]
        public void SuccessMarkupStringSource()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "Prompt" });
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void SuccessLabelSource()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "Caption" });
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void SuccessCopyText()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy", CopyText = "hello" });
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void NoSource()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy" });
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo(SourceInvalidMessage));
            ret[0].AssertFieldLocation("mod", "Copy", string.Empty);
        }

        [Test]
        public void BothSources()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "Prompt", CopyText = "hello" });
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo(SourceInvalidMessage));
        }

        [Test]
        public void NoMatchSourceField()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "Nothing" });
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Nothing' がモジュール 'mod' に存在しません。"));
            ret[0].AssertFieldLocation("mod", "Copy", "SourceField");
        }

        [Test]
        public void SourceFieldTypeMismatch()
        {
            var (designData, module) = CreateDesignDataWithSources();
            //ボタンはコピー元にできない
            var ret = Check(designData, module, new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "OtherButton" });
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("mod", "Copy", "SourceField");
        }
    }
}
