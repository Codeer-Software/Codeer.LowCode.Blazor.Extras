using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class FileValueConversionFieldDesignCheckTest
    {
        //mod: Customer (Text) / Owner (Link → Owners) / Csv1 (設定用)。EdiMap: EdiCode, CustomerCode。Owners: Id, Name
        static (DesignData, ModuleDesign) CreateDesignData()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new LinkFieldDesign { Name = "Owner", SearchCondition = new SearchCondition { ModuleName = "Owners" } });
            module.Fields.Add(new CsvFileFormatFieldDesign { Name = "Csv1" });

            var ediMap = Utilities.CreateModule("EdiMap");
            ediMap.Fields.Add(new TextFieldDesign { Name = "EdiCode" });
            ediMap.Fields.Add(new TextFieldDesign { Name = "CustomerCode" });
            designData.AddModule(ediMap);

            var owners = Utilities.CreateModule("Owners");
            owners.Fields.Add(new IdFieldDesign { Name = "Id" });
            owners.Fields.Add(new TextFieldDesign { Name = "Name" });
            designData.AddModule(owners);
            return (designData, module);
        }

        static FileValueConversionFieldDesign CreateField() => new()
        {
            Name = "Customer_Conversion",
            TargetField = "Customer",
            ConversionModule = "EdiMap",
            ExternalField = "EdiCode",
            InternalField = "CustomerCode",
        };

        static List<DesignCheckInfo> Check(DesignData designData, ModuleDesign module, FileValueConversionFieldDesign field)
        {
            module.Fields.Add(field);
            return field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
        }

        [Test]
        public void Success()
        {
            var (designData, module) = CreateDesignData();
            Assert.That(Check(designData, module, CreateField()), Is.Empty);
        }

        [Test]
        public void LinkField対象は変換表モジュールと内部値フィールドを省略できる()
        {
            var (designData, module) = CreateDesignData();
            var field = new FileValueConversionFieldDesign { Name = "Owner_Conversion", TargetField = "Owner", ExternalField = "Name" };
            Assert.That(Check(designData, module, field), Is.Empty);
        }

        [Test]
        public void TargetFieldRequired()
        {
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.TargetField = string.Empty;
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("変換対象フィールドを設定してください。"));
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "TargetField");
        }

        [Test]
        public void TargetFieldNotFound()
        {
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.TargetField = "Nope";
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Nope' がモジュール 'mod' に存在しません。"));
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "TargetField");
        }

        [Test]
        public void TargetFieldNotConvertible()
        {
            //値を持たない設定用フィールドは対象にできない
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.TargetField = "Csv1";
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Csv1' は変換対象にできません (値を持たないか、一括更新の対象外の型です)。"));
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "TargetField");
        }

        [Test]
        public void ConversionModuleRequired()
        {
            //LinkField 以外の対象では変換表モジュールを省略できない
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.ConversionModule = string.Empty;
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("変換表モジュールを設定してください (省略できるのは変換対象が LinkField のときだけです)。"));
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "ConversionModule");
        }

        [Test]
        public void ConversionModuleNotFound()
        {
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.ConversionModule = "Nope";
            var ret = Check(designData, module, field);
            //モジュール不在時はフィールド存在チェックはスキップされ、モジュール不在の1件のみ
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("モジュール 'Nope' が存在しません。"));
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "ConversionModule");
        }

        [Test]
        public void ExternalFieldRequiredAndInternalFieldRequired()
        {
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.ExternalField = string.Empty;
            field.InternalField = string.Empty;
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(ret[0].Message, Is.EqualTo("外部値フィールドを設定してください。"));
                Assert.That(ret[1].Message, Is.EqualTo("内部値フィールドを設定してください (省略できるのは変換対象が LinkField のときだけです)。"));
            });
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "ExternalField");
            ret[1].AssertFieldLocation("mod", "Customer_Conversion", "InternalField");
        }

        [Test]
        public void ConversionFieldsNotFound()
        {
            var (designData, module) = CreateDesignData();
            var field = CreateField();
            field.ExternalField = "Nope1";
            field.InternalField = "Nope2";
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Nope1' がモジュール 'EdiMap' に存在しません。"));
                Assert.That(ret[1].Message, Is.EqualTo("フィールド 'Nope2' がモジュール 'EdiMap' に存在しません。"));
            });
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "ExternalField");
            ret[1].AssertFieldLocation("mod", "Customer_Conversion", "InternalField");
        }

        [Test]
        public void LinkField対象の省略時既定値も存在を検査する()
        {
            //Owners に Name が無い → 既定値で解決したモジュール名で指摘
            var (designData, module) = CreateDesignData();
            var field = new FileValueConversionFieldDesign { Name = "Owner_Conversion", TargetField = "Owner", ExternalField = "Nope" };
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Nope' がモジュール 'Owners' に存在しません。"));
            ret[0].AssertFieldLocation("mod", "Owner_Conversion", "ExternalField");
        }

        [Test]
        public void ConversionModuleDiffersFromLinkTarget()
        {
            var (designData, module) = CreateDesignData();
            var field = new FileValueConversionFieldDesign
            {
                Name = "Owner_Conversion",
                TargetField = "Owner",
                ConversionModule = "EdiMap",
                ExternalField = "EdiCode",
                InternalField = "CustomerCode",
            };
            var ret = Check(designData, module, field);
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("変換表モジュール 'EdiMap' が変換対象フィールドのリンク先モジュール 'Owners' と異なります。"));
            ret[0].AssertFieldLocation("mod", "Owner_Conversion", "ConversionModule");
        }

        [Test]
        public void TargetFieldDuplicated()
        {
            var (designData, module) = CreateDesignData();
            var other = CreateField();
            other.Name = "Customer_Conversion_Other";
            module.Fields.Add(other);
            var ret = Check(designData, module, CreateField());
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Customer' を変換対象とするファイル値変換フィールドが他にもあります。変換対象 1 つにつき 1 つにしてください。"));
            ret[0].AssertFieldLocation("mod", "Customer_Conversion", "TargetField");
        }
    }
}
