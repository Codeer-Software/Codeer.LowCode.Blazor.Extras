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

            var fields = module!.Fields.OfType<TagFieldDesign>().ToList();
            Assert.That(fields.Select(e => e.Name), Is.EqualTo(new[] { "Tags", "TagsSpace", "TagsView" }));
            Assert.That(fields.Single(e => e.Name == "TagsSpace").ConfirmOnSpace, Is.True);
            Assert.That(fields.Single(e => e.Name == "TagsSpace").IsRequired, Is.True);
            Assert.That(module.Fields.OfType<ButtonFieldDesign>().Single().OnClick, Is.EqualTo("Copy_OnClick"));

            var context = new DesignCheckContext("TagTest", d, Utilities.CreateDataSource());
            foreach (var field in fields)
            {
                Assert.That(field.CheckDesign(context), Is.Empty, field.Name);
            }
        }
    }
}
