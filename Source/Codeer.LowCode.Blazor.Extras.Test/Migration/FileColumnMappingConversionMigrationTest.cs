using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Migrations;
using Codeer.LowCode.Blazor.Repository.Design;

#pragma warning disable CS0618 // 廃止されたコード変換プロパティを移行元として使う

namespace Codeer.LowCode.Blazor.Extras.Test.Migration
{
    /// <summary>
    /// 0.13.0: 列マッピングのコード変換 → FileValueConversionField への移行。
    /// </summary>
    public class FileColumnMappingConversionMigrationTest
    {
        static MappingColumn Converted(string field, string module = "EdiMap", string external = "EdiCode", string internalField = "CustomerCode") => new()
        {
            ExternalName = field,
            Field = field,
            ConversionModule = module,
            ConversionExternalField = external,
            ConversionInternalField = internalField,
        };

        static FileColumnMappingFieldDesign CreateMapping(params MappingColumn[] columns)
            => new() { Name = "Mapping1", Columns = new MappingColumns { Items = [.. columns] } };

        [Test]
        public void バージョンとタイトル()
        {
            var migration = new FileColumnMappingConversionMigration();
            Assert.Multiple(() =>
            {
                Assert.That(migration.Version, Is.EqualTo("0.13.0.0"));
                Assert.That(migration.Title, Is.Not.Empty);
                Assert.That(migration.Description, Is.Not.Empty);
            });
        }

        [Test]
        public void 変換付きの列ごとに変換フィールドを作り列側の設定を消す()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            var mapping = CreateMapping(
                Converted("Customer.Value"),
                new MappingColumn { ExternalName = "受注日", Field = "OrderDate.Value" },
                new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001", ConversionModule = "EdiMap", ConversionExternalField = "A", ConversionInternalField = "B" }); //Field 無し = 使われていない変換
            module.Fields.Add(mapping);

            new FileColumnMappingConversionMigration().Execute(designData);

            var conversions = module.Fields.OfType<FileValueConversionFieldDesign>().ToList();
            Assert.That(conversions, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(conversions[0].Name, Is.EqualTo("Customer_Conversion"));
                Assert.That(conversions[0].TargetField, Is.EqualTo("Customer"));
                Assert.That(conversions[0].ConversionModule, Is.EqualTo("EdiMap"));
                Assert.That(conversions[0].ExternalField, Is.EqualTo("EdiCode"));
                Assert.That(conversions[0].InternalField, Is.EqualTo("CustomerCode"));
                Assert.That(mapping.Columns.Items.All(c => !c.HasObsoleteConversion()), Is.True);
                Assert.That(mapping.Columns.Items[0].ConversionModule, Is.Null); //JSON から消える (null は書き出されない)
                Assert.That(mapping.Columns.Items[0].Field, Is.EqualTo("Customer.Value")); //列構成はそのまま
                Assert.That(mapping.Columns.Items[2].FixedValue, Is.EqualTo("JP0001"));
            });
        }

        [Test]
        public void 移行後の列は廃止プロパティがJSONに出ない()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var mapping = CreateMapping(Converted("Customer.Value"));
            module.Fields.Add(mapping);
            Assert.That(Codeer.LowCode.Blazor.Json.JsonConverterEx.SerializeObject(mapping), Does.Contain("ConversionModule"));

            new FileColumnMappingConversionMigration().Execute(designData);

            var json = Codeer.LowCode.Blazor.Json.JsonConverterEx.SerializeObject(mapping);
            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Not.Contain("ConversionModule"));
                Assert.That(json, Does.Not.Contain("ConversionExternalField"));
                Assert.That(json, Does.Not.Contain("ConversionInternalField"));
                Assert.That(json, Does.Contain("\"Field\":\"Customer.Value\"").Or.Contain("\"Field\": \"Customer.Value\""));
            });
        }

        [Test]
        public void 同じ対象に同じ変換が複数列あれば変換フィールドは1つ()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var mapping = CreateMapping(Converted("Customer.Value"), Converted("Customer.Value"));
            module.Fields.Add(mapping);

            new FileColumnMappingConversionMigration().Execute(designData);

            Assert.That(module.Fields.OfType<FileValueConversionFieldDesign>().Count(), Is.EqualTo(1));
            Assert.That(mapping.Columns.Items.All(c => !c.HasObsoleteConversion()), Is.True);
        }

        [Test]
        public void 同じ対象に違う変換が付いた列は残す()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var mapping = CreateMapping(Converted("Customer.Value"), Converted("Customer.Value", module: "OtherMap"));
            module.Fields.Add(mapping);

            new FileColumnMappingConversionMigration().Execute(designData);

            Assert.Multiple(() =>
            {
                Assert.That(module.Fields.OfType<FileValueConversionFieldDesign>().Count(), Is.EqualTo(1));
                Assert.That(mapping.Columns.Items[0].HasObsoleteConversion(), Is.False);
                Assert.That(mapping.Columns.Items[1].HasObsoleteConversion(), Is.True); //デザインチェックが指摘する
                Assert.That(mapping.Columns.Items[1].ConversionModule, Is.EqualTo("OtherMap"));
            });
        }

        [Test]
        public void 変換の無いマッピングは何もしない()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(CreateMapping(new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" }));

            new FileColumnMappingConversionMigration().Execute(designData);

            Assert.That(module.Fields.OfType<FileValueConversionFieldDesign>(), Is.Empty);
        }

        [Test]
        public void 名前が衝突すれば連番を付ける()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer_Conversion" });
            module.Fields.Add(CreateMapping(Converted("Customer.Value")));

            new FileColumnMappingConversionMigration().Execute(designData);

            Assert.That(module.Fields.OfType<FileValueConversionFieldDesign>().Single().Name, Is.EqualTo("Customer_Conversion2"));
        }
    }
}
