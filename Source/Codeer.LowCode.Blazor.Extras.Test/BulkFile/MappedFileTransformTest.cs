using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.BulkFile
{
    public class MappedFileTransformTest
    {
        //特殊書式フィールドのテストダブル。外部表現は <値>、解釈できない外部値は false を返す
        class ExoticFieldData() : ValueFieldDataBase<string>(typeof(ExoticFieldData).FullName!);

        class ExoticFieldDesign() : FieldDesignBase(typeof(ExoticFieldDesign).FullName!), IExternalTextFormatFieldDesign
        {
            public string FormatExternalText(object? value) => $"<{value}>";

            public bool TryParseExternalText(string externalText, out object? value)
            {
                if (externalText.StartsWith('<') && externalText.EndsWith('>'))
                {
                    value = externalText[1..^1];
                    return true;
                }
                value = null;
                return false;
            }

            public override string GetWebComponentTypeFullName() => string.Empty;
            public override string GetSearchWebComponentTypeFullName() => string.Empty;
            public override string GetSearchControlTypeFullName() => string.Empty;
            public override FieldDataBase? CreateData() => new ExoticFieldData();
            public override FieldBase CreateField() => throw new NotSupportedException();
        }

        //書式は列ではなくフィールド側 (Format プロパティ) の設定
        static ModuleDesign CreateModule()
        {
            var module = Utilities.CreateModule();
            module.Fields.Add(new DateFieldDesign { Name = "OrderDate", Format = "yyyyMMdd" });
            module.Fields.Add(new TimeFieldDesign { Name = "DeliveryTime", Format = "HHmm" });
            module.Fields.Add(new NumberFieldDesign { Name = "Qty", Format = "0000000" });
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new BooleanFieldDesign { Name = "Done" });
            module.Fields.Add(new ExoticFieldDesign { Name = "Exotic" });
            return module;
        }

        static ModuleData CreateItem(params (string Field, FieldDataBase Data)[] fields)
        {
            var item = new ModuleData { Name = "mod" };
            foreach (var f in fields) item.Fields[f.Field] = f.Data;
            return item;
        }

        //変換表 EdiMap (EdiCode ⇔ CustomerCode)
        static Task<List<List<string>>> GetConversionTableTexts(SearchCondition condition)
        {
            Assert.That(condition.ModuleName, Is.EqualTo("EdiMap"));
            return Task.FromResult<List<List<string>>>(
            [
                ["EdiCode.Value", "CustomerCode.Value"],
                ["A001", "C-0001"],
                ["A002", "C-0002"],
            ]);
        }

        static Task<List<List<string>>> NoTableTexts(SearchCondition condition)
            => throw new InvalidOperationException("変換表なしのテストで変換表が要求された");

        static MappingColumns Columns(params MappingColumn[] items) => new() { Items = [.. items] };

        [Test]
        public async Task ToExternalFormatsByFieldSettings()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(
                    new MappingColumn { ExternalName = "受注日", Field = "OrderDate.Value" },
                    new MappingColumn { ExternalName = "納品時刻", Field = "DeliveryTime.Value" },
                    new MappingColumn { ExternalName = "数量", Field = "Qty.Value" })
            };
            List<ModuleData> items =
            [
                CreateItem(
                    ("OrderDate", new DateFieldData { Value = new DateOnly(2026, 7, 17) }),
                    ("DeliveryTime", new TimeFieldData { Value = new TimeOnly(9, 30) }),
                    ("Qty", new NumberFieldData { Value = 123 })),
            ];
            var result = await MappedFileTransform.ToExternalAsync(items, design, CreateModule(), NoTableTexts);
            Assert.That(result, Is.EqualTo(new[]
            {
                new[] { "受注日", "納品時刻", "数量" },
                new[] { "20260717", "0930", "0000123" },
            }));
        }

        [Test]
        public async Task ToExternalUsesToStringForNonFormatFieldsAndEmptyForNull()
        {
            //書式を持たない型 (TextField 等) は ToString のみ。null 値は空文字
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    new MappingColumn { ExternalName = "受注日", Field = "OrderDate.Value" })
            };
            List<ModuleData> items =
            [
                CreateItem(("Customer", new TextFieldData { Value = "大阪, 北区" }), ("OrderDate", new DateFieldData())),
            ];
            var result = await MappedFileTransform.ToExternalAsync(items, design, CreateModule(), NoTableTexts);
            Assert.That(result[1], Is.EqualTo(new[] { "大阪, 北区", "" }));
        }

        [Test]
        public async Task ToExternalDelegatesToCustomFieldAndOutputsFixedValueWithoutHeader()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                HasHeader = false,
                Columns = Columns(
                    new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001" },
                    new MappingColumn { ExternalName = "特殊", Field = "Exotic.Value" })
            };
            List<ModuleData> items = [CreateItem(("Exotic", new ExoticFieldData { Value = "abc" }))];
            var result = await MappedFileTransform.ToExternalAsync(items, design, CreateModule(), NoTableTexts);
            //HasHeader = false なのでヘッダ行なし
            Assert.That(result, Is.EqualTo(new[] { new[] { "JP0001", "<abc>" } }));
        }

        [Test]
        public async Task ToExternalConvertsCodeWithoutApplyingFormat()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(new MappingColumn
                {
                    ExternalName = "得意先",
                    Field = "Customer.Value",
                    ConversionModule = "EdiMap",
                    ConversionExternalField = "EdiCode",
                    ConversionInternalField = "CustomerCode",
                })
            };
            List<ModuleData> items =
            [
                CreateItem(("Customer", new TextFieldData { Value = "C-0001" })),
                CreateItem(("Customer", new TextFieldData { Value = "C-0002" })),
            ];
            var result = await MappedFileTransform.ToExternalAsync(items, design, CreateModule(), GetConversionTableTexts);
            Assert.That(result.Skip(1), Is.EqualTo(new[] { new[] { "A001" }, new[] { "A002" } }));
        }

        [Test]
        public async Task ToInternalParsesToTypedValues()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(
                    new MappingColumn { ExternalName = "受注日", Field = "OrderDate.Value" },
                    new MappingColumn { ExternalName = "納品時刻", Field = "DeliveryTime.Value" },
                    new MappingColumn { ExternalName = "数量", Field = "Qty.Value" },
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001" }) //Field なしは取込対象外
            };
            List<List<string>> externalTexts =
            [
                ["受注日", "納品時刻", "数量", "得意先", "取引先"],
                ["20260717", "0930", "0001234", "20260717", "JP0001"],
            ];
            var (items, errors) = await MappedFileTransform.ToInternalAsync(externalTexts, design, CreateModule(), NoTableTexts);
            Assert.That(errors, Is.Empty);
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(((DateFieldData)items[0].Fields["OrderDate"]).Value, Is.EqualTo(new DateOnly(2026, 7, 17)));
                Assert.That(((TimeFieldData)items[0].Fields["DeliveryTime"]).Value, Is.EqualTo(new TimeOnly(9, 30)));
                Assert.That(((NumberFieldData)items[0].Fields["Qty"]).Value, Is.EqualTo(1234m));
                Assert.That(((TextFieldData)items[0].Fields["Customer"]).Value, Is.EqualTo("20260717")); //TextField はそのまま
                Assert.That(items[0].Fields, Has.Count.EqualTo(4)); //固定値列は取り込まれない
            });
        }

        [Test]
        public async Task ToInternalReportsUnmatchedCodeWithRowNumber()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(new MappingColumn
                {
                    ExternalName = "得意先",
                    Field = "Customer.Value",
                    ConversionModule = "EdiMap",
                    ConversionExternalField = "EdiCode",
                    ConversionInternalField = "CustomerCode",
                })
            };
            List<List<string>> externalTexts = [["得意先"], ["A001"], ["ZZZ"]];
            var (items, errors) = await MappedFileTransform.ToInternalAsync(externalTexts, design, CreateModule(), GetConversionTableTexts);
            Assert.Multiple(() =>
            {
                Assert.That(((TextFieldData)items[0].Fields["Customer"]).Value, Is.EqualTo("C-0001"));
                Assert.That(errors, Is.EqualTo(new[] { "Row 3, 得意先: code 'ZZZ' was not found in 'EdiMap'." }));
            });
        }

        [Test]
        public async Task ToInternalReportsCustomFieldParseFailureWithRowNumber()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                HasHeader = false,
                Columns = Columns(new MappingColumn { ExternalName = "特殊", Field = "Exotic.Value" })
            };
            List<List<string>> externalTexts = [["<abc>"], ["broken"]];
            var (items, errors) = await MappedFileTransform.ToInternalAsync(externalTexts, design, CreateModule(), NoTableTexts);
            Assert.Multiple(() =>
            {
                //HasHeader = false なので1行目からデータ (行番号も1から)
                Assert.That(((ExoticFieldData)items[0].Fields["Exotic"]).Value, Is.EqualTo("abc"));
                Assert.That(errors, Is.EqualTo(new[] { "Row 2, 特殊: cannot parse 'broken'." }));
            });
        }

        [Test]
        public async Task ToInternalReportsFormatMismatchWithRowNumber()
        {
            //フィールドの書式どおりに解釈できない値は行番号付きエラー (相手仕様の書式を強制する)
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(new MappingColumn { ExternalName = "受注日", Field = "OrderDate.Value" })
            };
            List<List<string>> externalTexts = [["受注日"], ["2026年07月17日"]];
            var (_, errors) = await MappedFileTransform.ToInternalAsync(externalTexts, design, CreateModule(), NoTableTexts);
            Assert.That(errors, Is.EqualTo(new[] { "Row 2, 受注日: cannot parse '2026年07月17日'." }));
        }

        [Test]
        public async Task ToInternalReportsTypeConversionFailureWithRowNumber()
        {
            //書式を持たない型でも、型変換できない値は黙って null にせず行番号付きエラー
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(new MappingColumn { ExternalName = "完了", Field = "Done.Value" })
            };
            List<List<string>> externalTexts = [["完了"], ["xyz"]];
            var (_, errors) = await MappedFileTransform.ToInternalAsync(externalTexts, design, CreateModule(), NoTableTexts);
            Assert.That(errors, Is.EqualTo(new[] { "Row 2, 完了: cannot convert 'xyz'." }));
        }

        [Test]
        public async Task ToInternalEmptyCellBecomesNullWithoutError()
        {
            var design = new MappedFileTransferFieldDesign
            {
                Name = "Mapping1",
                Columns = Columns(new MappingColumn { ExternalName = "受注日", Field = "OrderDate.Value" })
            };
            List<List<string>> externalTexts = [["受注日"], [""]];
            var (items, errors) = await MappedFileTransform.ToInternalAsync(externalTexts, design, CreateModule(), NoTableTexts);
            Assert.Multiple(() =>
            {
                Assert.That(errors, Is.Empty);
                Assert.That(((DateFieldData)items[0].Fields["OrderDate"]).Value, Is.Null);
            });
        }
    }
}
