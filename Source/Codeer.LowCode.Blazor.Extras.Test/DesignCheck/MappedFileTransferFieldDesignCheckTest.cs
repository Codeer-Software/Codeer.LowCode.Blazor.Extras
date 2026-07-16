using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class MappedFileTransferFieldDesignCheckTest
    {
        static MappedFileTransferFieldDesign CreateField() => new()
        {
            Name = "Mapping1",
            Columns = new MappingColumns
            {
                Items =
                [
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001" },
                    new MappingColumn
                    {
                        ExternalName = "得意先コード",
                        Field = "Customer.Value",
                        ConversionModule = "EdiMap",
                        ConversionExternalField = "EdiCode",
                        ConversionInternalField = "CustomerCode"
                    }
                ]
            }
        };

        [Test]
        public void Success()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            var ediMap = Utilities.CreateModule("EdiMap");
            ediMap.Fields.Add(new TextFieldDesign { Name = "EdiCode" });
            ediMap.Fields.Add(new TextFieldDesign { Name = "CustomerCode" });
            designData.AddModule(ediMap);

            var field = CreateField();
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void NoMatchOwnField()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var ediMap = Utilities.CreateModule("EdiMap");
            ediMap.Fields.Add(new TextFieldDesign { Name = "EdiCode" });
            ediMap.Fields.Add(new TextFieldDesign { Name = "CustomerCode" });
            designData.AddModule(ediMap);

            //自モジュールに Customer が存在しない
            var field = CreateField();
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Customer' がモジュール 'mod' に存在しません。"));
                Assert.That(ret[1].Message, Is.EqualTo("フィールド 'Customer' がモジュール 'mod' に存在しません。"));
            });
            ret[0].AssertFieldLocation("mod", "Mapping1", "Columns[0]");
            ret[1].AssertFieldLocation("mod", "Mapping1", "Columns[2]");
        }

        [Test]
        public void NoMatchConversionModule()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });

            //EdiMap モジュールが存在しない
            var field = CreateField();
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            //モジュール不在時はフィールド存在チェックはスキップされ、モジュール不在の1件のみ
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("モジュール 'EdiMap' が存在しません。"));
            ret[0].AssertFieldLocation("mod", "Mapping1", "Columns[2]");
        }

        [Test]
        public void NoMatchConversionFields()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            var ediMap = Utilities.CreateModule("EdiMap");
            designData.AddModule(ediMap);

            //変換表モジュールはあるがフィールドがない
            var field = CreateField();
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(ret[0].Message, Is.EqualTo("フィールド 'EdiCode' がモジュール 'EdiMap' に存在しません。"));
                Assert.That(ret[1].Message, Is.EqualTo("フィールド 'CustomerCode' がモジュール 'EdiMap' に存在しません。"));
            });
            ret[0].AssertFieldLocation("mod", "Mapping1", "Columns[2]");
            ret[1].AssertFieldLocation("mod", "Mapping1", "Columns[2]");
        }
    }
}
