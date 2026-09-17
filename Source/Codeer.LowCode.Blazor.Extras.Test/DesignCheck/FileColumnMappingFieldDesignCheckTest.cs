using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class FileColumnMappingFieldDesignCheckTest
    {
        static FileColumnMappingFieldDesign CreateField() => new()
        {
            Name = "Mapping1",
            Columns = new MappingColumns
            {
                Items =
                [
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001" },
                    new MappingColumn { ExternalName = "得意先コード", Field = "Customer.Value" }
                ]
            }
        };

        [Test]
        public void Success()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });

            var field = CreateField();
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void NoMatchOwnField()
        {
            var (designData, module) = Utilities.CreateDesignData();

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
        public void 廃止されたコード変換が残っている列はエラー()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });

            var field = CreateField();
#pragma warning disable CS0618 // 廃止プロパティが残っている古いデザインを再現
            field.Columns.Items[2].ConversionModule = "EdiMap";
            field.Columns.Items[2].ConversionExternalField = "EdiCode";
            field.Columns.Items[2].ConversionInternalField = "CustomerCode";
#pragma warning restore CS0618
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("列マッピングのコード変換 (ConversionModule / ConversionExternalField / ConversionInternalField) は廃止されました。ファイル値変換フィールドへ移してください (デザイナのマイグレーション「ファイル列マッピングのコード変換をファイル値変換フィールドへ移行」で移せます)。"));
            Assert.That(ret[0].Code, Is.EqualTo("FileColumnMappingFieldDesign:3"));
            ret[0].AssertFieldLocation("mod", "Mapping1", "Columns[2]");
        }

        //固定長形式用の列設定 (全列 Width あり・ゼロ埋めは右寄せ)。
        //固定長かどうかは同じモジュールの CsvFileFormatField の Delimiter (None = 固定長) で決まる
        static FileColumnMappingFieldDesign CreateFixedLengthField() => new()
        {
            Name = "Mapping1",
            Columns = new MappingColumns
            {
                Items =
                [
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value", FixedLengthWidth = 10 },
                    new MappingColumn
                    {
                        ExternalName = "数量", Field = "Customer.Value", FixedLengthWidth = 5,
                        FixedLengthAlignment = FixedLengthAlignmentKind.Right, FixedLengthPaddingChar = FixedLengthPaddingCharKind.Zero
                    },
                    new MappingColumn { FixedLengthWidth = 4 } //ブランク列にも幅は必要
                ]
            }
        };

        [Test]
        public void FixedLengthSuccess()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new CsvFileFormatFieldDesign { Name = "Csv1", Delimiter = CsvDelimiterKind.None });

            var field = CreateFixedLengthField();
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void FixedLengthWidthRequired()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new CsvFileFormatFieldDesign { Name = "Csv1", Delimiter = CsvDelimiterKind.None });

            var field = CreateFixedLengthField();
            field.Columns.Items[2].FixedLengthWidth = 0;
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("固定長形式では全ての列に幅 (1以上) を設定してください。"));
            ret[0].AssertFieldLocation("mod", "Mapping1", "Columns[2]");
        }

        [Test]
        public void FixedLengthZeroPaddingRequiresRight()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new CsvFileFormatFieldDesign { Name = "Csv1", Delimiter = CsvDelimiterKind.None });

            var field = CreateFixedLengthField();
            field.Columns.Items[1].FixedLengthAlignment = FixedLengthAlignmentKind.Left; //ゼロ埋めのまま左寄せ
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("ゼロ埋めは右寄せの列でのみ使用できます (左寄せのゼロ埋めは値の末尾の 0 と区別できません)。"));
            ret[0].AssertFieldLocation("mod", "Mapping1", "Columns[1]");
        }

        [Test]
        public void 非固定長なら列のWidth未設定でもエラーにしない()
        {
            //従来の組み合わせ (CSV/xlsx) では列幅は使われないためチェックしない
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new CsvFileFormatFieldDesign { Name = "Csv1" }); //Delimiter = Comma (既定) = 非固定長

            var field = CreateFixedLengthField();
            field.Columns.Items[2].FixedLengthWidth = 0;
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }
    }
}
