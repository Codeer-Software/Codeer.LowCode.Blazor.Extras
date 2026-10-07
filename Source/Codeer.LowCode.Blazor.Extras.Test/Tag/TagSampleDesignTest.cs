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

            //Tags はタグ付け (tag-setup が作った TagTestTags) に保存する。他の 2 つは Tags のタグから候補を出すだけの入力欄
            var tags = module!.Fields.OfType<TagFieldDesign>().Single();
            Assert.That(tags.Name, Is.EqualTo("Tags"));
            Assert.That(tags.SearchCondition.ModuleName, Is.EqualTo("TagTestTags"));
            var inputs = module.Fields.OfType<TagInputFieldDesign>().ToList();
            Assert.That(inputs.Select(e => e.Name), Is.EqualTo(new[] { "TagsSpace", "TagsView" }));
            Assert.That(inputs.Select(e => (e.CandidateSource, e.CandidateModuleName, e.CandidateFieldName)), Is.All.EqualTo((TagCandidateSource.Module, "TagTest", "Tags")));
            Assert.That(inputs[0].ConfirmOnSpace, Is.True);
            Assert.That(inputs[0].IsRequired, Is.True);
            Assert.That(module.Fields.OfType<ButtonFieldDesign>().Single().OnClick, Is.EqualTo("Copy_OnClick"));
            Assert.That(module.ListLayouts[""].Elements[0].Select(e => e.FieldName), Does.Contain("Tags"), "一覧の列にも置ける");
            Assert.That(d.Modules.Find("Tag"), Is.Null, "タグのマスタは無い");

            var noTables = new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>();
            foreach (var field in inputs.Cast<FieldDesignBase>().Prepend(tags))
            {
                Assert.That(field.CheckDesign(new DesignCheckContext("TagTest", d, noTables)), Is.Empty, field.Name);
            }
            Assert.That(d.Modules.Find("TagTestTags")!.Fields.OfType<TagLinkContractFieldDesign>().Single().CheckDesign(new DesignCheckContext("TagTestTags", d, noTables)), Is.Empty);
        }
    }
}
