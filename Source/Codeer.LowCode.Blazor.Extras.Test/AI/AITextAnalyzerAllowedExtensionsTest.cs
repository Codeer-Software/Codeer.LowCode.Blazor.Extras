using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// AITextAnalyzerField が受け付ける拡張子の解決。
    /// 連携 FileField の AllowedExtensions → 自身の AllowedExtensions → ドキュメント解析の既定 の順で決まることを固定する。
    /// </summary>
    public class AITextAnalyzerAllowedExtensionsTest
    {
        static ModuleDesign CreateModule(string fileFieldExtensions)
        {
            var module = new ModuleDesign { Name = "Order" };
            module.Fields.Add(new FileFieldDesign { Name = "Attachment", AllowedExtensions = fileFieldExtensions });
            return module;
        }

        [Test]
        public void 既定はドキュメント解析が扱える形式()
        {
            var design = new AITextAnalyzerFieldDesign { Name = "Analyzer" };
            Assert.That(design.GetAllowedExtensions(null), Is.EqualTo(AITextAnalyzerFieldDesign.DocumentAnalysisExtensions));
            Assert.That(design.IsAllowedFileName(null, "a.pdf"), Is.True);
            Assert.That(design.IsAllowedFileName(null, "a.PNG"), Is.True);
            Assert.That(design.IsAllowedFileName(null, "a.exe"), Is.False);
        }

        [Test]
        public void 自身の設定があればそれに従う()
        {
            var design = new AITextAnalyzerFieldDesign { Name = "Analyzer", AllowedExtensions = "pdf" };
            Assert.That(design.GetAllowedExtensions(CreateModule("")), Is.EqualTo(new[] { "pdf" }));
            Assert.That(design.IsAllowedFileName(null, "a.png"), Is.False);
        }

        [Test]
        public void 連携FileFieldの設定が優先される()
        {
            var design = new AITextAnalyzerFieldDesign { Name = "Analyzer", FileField = "Attachment", AllowedExtensions = "pdf" };
            Assert.That(design.GetAllowedExtensions(CreateModule("jpg, png")), Is.EqualTo(new[] { "jpg", "png" }));
            Assert.That(design.IsAllowedFileName(CreateModule("jpg, png"), "a.pdf"), Is.False);
            Assert.That(design.IsAllowedFileName(CreateModule("jpg, png"), "a.jpg"), Is.True);

            //連携 FileField が無制限なら自身の設定
            Assert.That(design.GetAllowedExtensions(CreateModule("")), Is.EqualTo(new[] { "pdf" }));
        }
    }
}
