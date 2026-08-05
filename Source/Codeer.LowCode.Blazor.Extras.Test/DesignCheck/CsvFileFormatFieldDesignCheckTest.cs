using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class CsvFileFormatFieldDesignCheckTest
    {
        [Test]
        public void Success()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new CsvFileFormatFieldDesign { Name = "Csv1" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void FixedLengthSuccess()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new CsvFileFormatFieldDesign { Name = "Csv1", Delimiter = CsvDelimiterKind.None };
            module.Fields.Add(field);
            module.Fields.Add(new FileColumnMappingFieldDesign { Name = "Mapping1" });
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void FixedLengthMappingRequired()
        {
            //固定長形式は列幅の置き場である列マッピングが無いと成立しない
            var (designData, module) = Utilities.CreateDesignData();
            var field = new CsvFileFormatFieldDesign { Name = "Csv1", Delimiter = CsvDelimiterKind.None };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("固定長形式 (区切り文字 None) には同じモジュールにファイル列マッピングフィールドが必要です (列幅はそちらの列ごとに設定します)。"));
            ret[0].AssertFieldLocation("mod", "Csv1", "Delimiter");
        }
    }
}
