using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.Csv;
using Excel.Report.PDF;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.BulkFile
{
    /// <summary>
    /// 固定長形式の生成/取込 (FixedLengthUtils)。
    /// 形式 (幅の単位・エンコーディング) は CsvFileFormatField、列幅・寄せ・パディングは FileColumnMappingField の列定義。
    /// パディング/寄せ/幅の単位 (Byte は Shift_JIS の全角 2 バイト計測)、
    /// 幅あふれの行番号付きエラー (黙って切り詰めない)、取込の寛容さ (短い行・BOM・xlsx 自動判定) を固定する。
    /// </summary>
    public class FixedLengthUtilsTest
    {
        static FileColumnMappingFieldDesign CreateColumns(params MappingColumn[] cols)
            => new()
            {
                Name = "Mapping1",
                HasHeader = false,
                Columns = new MappingColumns { Items = [.. cols] }
            };

        static CsvFileFormatFieldDesign CreateFormat(FixedLengthWidthUnitKind unit, CsvEncodingKind encoding)
            => new() { Name = "Format1", Delimiter = CsvDelimiterKind.None, FixedLengthWidthUnit = unit, Encoding = encoding };

        static string ToText(MemoryStream ms, Encoding encoding) => encoding.GetString(ms.ToArray());

        [Test]
        public void 出力_文字数単位_左右寄せとパディング()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Name", FixedLengthWidth = 5 },
                new MappingColumn { ExternalName = "Qty", FixedLengthWidth = 5, FixedLengthAlignment = FixedLengthAlignmentKind.Right, FixedLengthPaddingChar = FixedLengthPaddingCharKind.Zero });

            var ms = FixedLengthUtils.CreateFixedLengthBinary([["abc", "12"]], columns, CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8));

            Assert.That(ToText(ms, new UTF8Encoding(false)), Is.EqualTo("abc  00012\r\n"));
        }

        [Test]
        public void 出力_バイト単位_ShiftJISの全角は2バイト()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Name", FixedLengthWidth = 6 },
                new MappingColumn { ExternalName = "Code", FixedLengthWidth = 4, FixedLengthAlignment = FixedLengthAlignmentKind.Right, FixedLengthPaddingChar = FixedLengthPaddingCharKind.Zero });

            var ms = FixedLengthUtils.CreateFixedLengthBinary([["あい", "7"]], columns, CreateFormat(FixedLengthWidthUnitKind.Byte, CsvEncodingKind.ShiftJis));

            var sjis = Encoding.GetEncoding("shift_jis");
            //"あい" = 4 バイト + 空白 2 バイトで幅 6
            Assert.That(ToText(ms, sjis), Is.EqualTo("あい  0007\r\n"));
            Assert.That(ms.ToArray().Length, Is.EqualTo(6 + 4 + 2));
        }

        [Test]
        public void 出力_ブランク列と固定値列も幅を占める()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Name", FixedLengthWidth = 3 },
                new MappingColumn { FixedLengthWidth = 4 }, //ブランク列 (Field も FixedValue も空)
                new MappingColumn { ExternalName = "Fix", FixedLengthWidth = 2 });

            var ms = FixedLengthUtils.CreateFixedLengthBinary([["ab", "", "XY"]], columns, CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8));

            Assert.That(ToText(ms, new UTF8Encoding(false)), Is.EqualTo("ab     XY\r\n"));
        }

        [Test]
        public void 出力_幅あふれは行番号付きエラーで切り詰めない()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Name", FixedLengthWidth = 3 });

            var ex = Assert.Throws<LowCodeException>(() =>
                FixedLengthUtils.CreateFixedLengthBinary([["ok!"], ["too long"]], columns, CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8)));

            Assert.That(ex!.Message, Is.EqualTo("Row 2, Name: 'too long' exceeds the width 3."));
        }

        [Test]
        public void 取込_ラウンドトリップ_バイト単位ShiftJIS()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Name", FixedLengthWidth = 8 },
                new MappingColumn { ExternalName = "Qty", FixedLengthWidth = 5, FixedLengthAlignment = FixedLengthAlignmentKind.Right, FixedLengthPaddingChar = FixedLengthPaddingCharKind.Zero },
                new MappingColumn { ExternalName = "Note", FixedLengthWidth = 6, FixedLengthAlignment = FixedLengthAlignmentKind.Right });
            var format = CreateFormat(FixedLengthWidthUnitKind.Byte, CsvEncodingKind.ShiftJis);

            List<List<string>> texts = [["あいう", "120", "ok"], ["x", "5", ""]];
            var ms = FixedLengthUtils.CreateFixedLengthBinary(texts, columns, format);
            var readBack = FixedLengthUtils.ReadAllTextsFromFixedLength(ms, columns, format);

            Assert.That(readBack, Is.EqualTo(texts));
        }

        [Test]
        public void 取込_ヘッダ行はテーブルテキストにそのまま残る()
        {
            //ヘッダの読み飛ばしは列マッピング変換 (ToInternalAsync) の責務。ここでは行として読めることだけ
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Nm", FixedLengthWidth = 4 });
            columns.HasHeader = true;
            var format = CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8);

            List<List<string>> texts = [["Nm"], ["ab"]];
            var ms = FixedLengthUtils.CreateFixedLengthBinary(texts, columns, format);
            var readBack = FixedLengthUtils.ReadAllTextsFromFixedLength(ms, columns, format);

            Assert.That(readBack, Is.EqualTo(texts));
        }

        [Test]
        public void 取込_短い行は足りない列が空になる()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "A", FixedLengthWidth = 3 },
                new MappingColumn { ExternalName = "B", FixedLengthWidth = 3 });

            var ms = new MemoryStream(new UTF8Encoding(false).GetBytes("abc\r\n"));
            var readBack = FixedLengthUtils.ReadAllTextsFromFixedLength(ms, columns, CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8));

            Assert.That(readBack, Is.EqualTo((List<List<string>>)[["abc", ""]]));
        }

        [Test]
        public void 取込_ゼロ埋めの全ゼロは値0()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "Qty", FixedLengthWidth = 5, FixedLengthAlignment = FixedLengthAlignmentKind.Right, FixedLengthPaddingChar = FixedLengthPaddingCharKind.Zero });

            var ms = new MemoryStream(new UTF8Encoding(false).GetBytes("00000\r\n"));
            var readBack = FixedLengthUtils.ReadAllTextsFromFixedLength(ms, columns, CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8));

            Assert.That(readBack, Is.EqualTo((List<List<string>>)[["0"]]));
        }

        [Test]
        public void 取込_Utf8Bomは読み飛ばす()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "A", FixedLengthWidth = 3 });
            var format = CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8Bom);

            List<List<string>> texts = [["abc"]];
            var ms = FixedLengthUtils.CreateFixedLengthBinary(texts, columns, format);
            //StreamWriter が BOM を書いていることを前提に、読み戻しで残らないこと
            Assert.That(ms.ToArray()[0], Is.EqualTo(0xEF));

            var readBack = FixedLengthUtils.ReadAllTextsFromFixedLength(ms, columns, format);
            Assert.That(readBack, Is.EqualTo(texts));
        }

        [Test]
        public async Task 取込_xlsxは内容で自動判定してExcelとして読む()
        {
            var columns = CreateColumns(
                new MappingColumn { ExternalName = "A", FixedLengthWidth = 3 });

            List<List<string>> texts = [["ab", "cd"]];
            var excel = ExcelUtils.CreateExcelBinary(texts, "data");
            excel.Position = 0;

            var readBack = await FixedLengthUtils.ReadAllTextsFromFileBinary(excel, columns, CreateFormat(FixedLengthWidthUnitKind.Char, CsvEncodingKind.Utf8));
            Assert.That(readBack, Is.EqualTo(texts));
        }
    }
}
