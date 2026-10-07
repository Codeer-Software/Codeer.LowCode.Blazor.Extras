using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>Example の TagTest (TagField のサンプル画面) が読み込めて、デザインチェックを通ること。</summary>
    public class TagSampleDesignTest
    {
        static string DesignDir
            => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Example", "Design"));

        //GetDesignData は App.zip を読む形式のため、リポジトリのデザインフォルダを一時 zip にして読む
        static DesignData Load()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"tag_design_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(DesignDir, Path.Combine(tempDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(tempDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Test]
        public void サンプルのTagTestが読み込めてデザインチェックを通る()
        {
            var d = Load();
            var module = d.Modules.Find("TagTest");
            Assert.That(module, Is.Not.Null);

            //Tags はタグ付け (tag-setup が作った TagTestTags) に保存する。TagField は 1 つだけ
            var tags = module!.Fields.OfType<TagFieldDesign>().Single();
            Assert.That(tags.Name, Is.EqualTo("Tags"));
            Assert.That(tags.SearchCondition.ModuleName, Is.EqualTo("TagTestTags"));
            Assert.That(module.Fields.OfType<ButtonFieldDesign>().Single().OnClick, Is.EqualTo("Check_OnClick"));
            Assert.That(module.ListLayouts[""].Elements[0].Select(e => e.FieldName), Does.Contain("Tags"), "一覧の列にも置ける");
            Assert.That(d.Modules.Find("Tag"), Is.Null, "タグのマスタは無い");
            var name = (TextFieldDesign)d.Modules.Find("TagTestTags")!.Fields.Single(e => e.Name == "Name");
            Assert.That(name.MaxLength, Is.EqualTo(Codeer.LowCode.Blazor.Extras.Fields.TagField.MaxTagLength));

            var noTables = new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>();
            Assert.That(tags.CheckDesign(new DesignCheckContext("TagTest", d, noTables)), Is.Empty);
            Assert.That(d.Modules.Find("TagTestTags")!.Fields.OfType<TagLinkContractFieldDesign>().Single().CheckDesign(new DesignCheckContext("TagTestTags", d, noTables)), Is.Empty);
        }
    }
}
