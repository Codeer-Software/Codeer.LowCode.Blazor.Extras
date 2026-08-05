using Codeer.LowCode.Blazor.Extras.BulkFile;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Test.BulkFile
{
    /// <summary>
    /// スクリプト取込用の解析 (ParseFileAsync / ToInternalWithCellErrorsAsync)。
    /// 解釈できないセルは値未設定 + 構造化エラー (ItemIndex/FieldName) で報告し、行は捨てない
    /// (一括更新の「エラーがあれば取り込まない」と違う点) を固定する。
    /// </summary>
    public class BulkFileParseTest
    {
        static Task<List<List<string>>> NoTableTexts(SearchCondition condition)
            => throw new InvalidOperationException("変換表なしのテストで変換表が要求された");

        [Test]
        public async Task マッピング取込のセルエラーは構造化されて行は捨てない()
        {
            var module = Utilities.CreateModule();
            module.Fields.Add(new NumberFieldDesign { Name = "Qty" });
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });

            var design = new FileColumnMappingFieldDesign
            {
                Name = "Mapping1",
                HasHeader = false,
                Columns = new MappingColumns
                {
                    Items =
                    [
                        new MappingColumn { ExternalName = "数量", Field = "Qty.Value" },
                        new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    ]
                }
            };

            var (items, errors) = await FileColumnMappingTransform.ToInternalWithCellErrorsAsync(
                [["5", "A"], ["x", "B"]], design, module, NoTableTexts);

            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(errors[0].ItemIndex, Is.EqualTo(1));
                Assert.That(errors[0].FileRow, Is.EqualTo(2));
                Assert.That(errors[0].FieldName, Is.EqualTo("Qty"));
                Assert.That(errors[0].ColumnLabel, Is.EqualTo("数量"));
                //NumberField は書式パス (IExternalTextFormatFieldDesign) なので parse エラーになる
                Assert.That(errors[0].Message, Is.EqualTo("cannot parse 'x'."));
            });
            //エラーセルは値未設定、同じ行の他のセルは取り込まれる
            Assert.That(items[0].Fields.ContainsKey("Qty"), Is.True);
            Assert.That(items[1].Fields.ContainsKey("Qty"), Is.False);
            Assert.That(items[1].Fields.ContainsKey("Customer"), Is.True);
        }

        [Test]
        public async Task 列マッピングなしは内部名ヘッダで解析する()
        {
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new NumberFieldDesign { Name = "Qty" });
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });

            //未知の列は無視。型変換できないセルは構造化エラー
            List<List<string>> texts =
            [
                ["Qty.Value", "Customer.Value", "Unknown.Value"],
                ["5", "A", "z"],
                ["x", "B", "z"],
            ];
            var file = ExcelUtils.CreateExcelBinary(texts, "data");
            file.Position = 0;

            var result = await BulkFileTransfer.ParseFileAsync(designData, null!, "mod", file);

            Assert.That(result.Items.Count, Is.EqualTo(2));
            Assert.That(result.Errors.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(result.Errors[0].ItemIndex, Is.EqualTo(1));
                Assert.That(result.Errors[0].FileRow, Is.EqualTo(3));
                Assert.That(result.Errors[0].FieldName, Is.EqualTo("Qty"));
                Assert.That(result.Errors[0].ColumnLabel, Is.EqualTo("Qty.Value"));
            });
            Assert.That(result.Items[0].Fields.ContainsKey("Qty"), Is.True);
            Assert.That(result.Items[1].Fields.ContainsKey("Qty"), Is.False);
            Assert.That(result.Items[1].Fields.ContainsKey("Customer"), Is.True);
        }

        [Test]
        public async Task モジュールが無ければ空の結果()
        {
            var (designData, _) = Utilities.CreateDesignData();
            var result = await BulkFileTransfer.ParseFileAsync(designData, null!, "NotExist", new MemoryStream());
            Assert.That(result.Items.Count, Is.EqualTo(0));
            Assert.That(result.Errors.Count, Is.EqualTo(0));
        }
    }
}
