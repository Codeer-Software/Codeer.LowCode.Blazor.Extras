using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class ExcelReportButtonFieldDesignCheckTest
    {
        [Test]
        public void Success()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new ExcelReportButtonFieldDesign
            {
                Name = "Report",
                TemplateResourcePath = "Reports/Quotation.xlsx"
            };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void TemplateRequired()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new ExcelReportButtonFieldDesign { Name = "Report" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("テンプレートリソースパスを設定してください。"));
            ret[0].AssertFieldLocation("mod", "Report", "TemplateResourcePath");
        }
    }
}
