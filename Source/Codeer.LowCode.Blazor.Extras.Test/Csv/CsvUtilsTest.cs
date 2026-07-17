using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Csv;
using Codeer.LowCode.Blazor.Repository.Design;
using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Test.Csv
{
    public class CsvUtilsTest
    {
        static readonly List<List<string>> TrickyTexts =
        [
            ["Name.Value", "Address.Value", "Memo.Value"],
            ["山田\"太郎\"", "大阪, 北区", "1行目\r\n2行目"],
            ["セミコロン;入り", "タブ\t入り", ""],
        ];

        [Test]
        public void RoundTripUtf8Bom()
        {
            using var ms = CsvUtils.CreateCsvBinary(TrickyTexts, CsvEncodingKind.Utf8Bom);
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.Utf8Bom);
            Assert.That(rows, Is.EqualTo(TrickyTexts));
        }

        [Test]
        public void RoundTripShiftJis()
        {
            using var ms = CsvUtils.CreateCsvBinary(TrickyTexts, CsvEncodingKind.ShiftJis);
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.ShiftJis);
            Assert.That(rows, Is.EqualTo(TrickyTexts));
        }

        [Test]
        public void RoundTripTabDelimiter()
        {
            using var ms = CsvUtils.CreateCsvBinary(TrickyTexts, CsvEncodingKind.Utf8Bom, '\t');
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.Utf8Bom, '\t');
            Assert.That(rows, Is.EqualTo(TrickyTexts));
        }

        [Test]
        public void RoundTripSemicolonDelimiter()
        {
            using var ms = CsvUtils.CreateCsvBinary(TrickyTexts, CsvEncodingKind.Utf8Bom, ';');
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.Utf8Bom, ';');
            Assert.That(rows, Is.EqualTo(TrickyTexts));
        }

        [Test]
        public void Utf8BomWritesPreamble()
        {
            using var ms = CsvUtils.CreateCsvBinary([["a"]], CsvEncodingKind.Utf8Bom);
            var bytes = ms.ToArray();
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
        }

        [Test]
        public void Utf8WritesNoPreamble()
        {
            using var ms = CsvUtils.CreateCsvBinary([["a"]], CsvEncodingKind.Utf8);
            var bytes = ms.ToArray();
            Assert.That(bytes[0], Is.EqualTo((byte)'a'));
        }

        [Test]
        public void ShiftJisWritesNoPreambleAndSjisBytes()
        {
            using var ms = CsvUtils.CreateCsvBinary([["東"]], CsvEncodingKind.ShiftJis);
            var bytes = ms.ToArray();
            //"東" の Shift_JIS は 0x93 0x8C
            Assert.That(bytes.Take(2), Is.EqualTo(new byte[] { 0x93, 0x8C }));
        }

        [Test]
        public void BomOverridesConfiguredEncodingOnRead()
        {
            //設定が ShiftJis でも BOM 付き UTF-8 のファイルは正しく読める (BOM 自動判別)
            using var ms = CsvUtils.CreateCsvBinary(TrickyTexts, CsvEncodingKind.Utf8Bom);
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.ShiftJis);
            Assert.That(rows, Is.EqualTo(TrickyTexts));
        }

        [Test]
        public async Task ReadAllTextsFromFileBinaryAutoDetectsExcel()
        {
            List<List<string>> texts = [["Name.Value", "Memo.Value"], ["山田", "テスト"]];
            using var xlsx = ExcelUtils.CreateExcelBinary(texts, "data");
            var rows = await CsvUtils.ReadAllTextsFromFileBinary(xlsx, CsvEncodingKind.Utf8Bom);
            Assert.That(rows, Is.EqualTo(texts));
        }

        [Test]
        public async Task ReadAllTextsFromFileBinaryReadsCsv()
        {
            using var csv = CsvUtils.CreateCsvBinary(TrickyTexts, CsvEncodingKind.Utf8Bom);
            var rows = await CsvUtils.ReadAllTextsFromFileBinary(csv, CsvEncodingKind.Utf8Bom);
            Assert.That(rows, Is.EqualTo(TrickyTexts));
        }

        [Test]
        public void DelimiterToChar()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new CsvFileFormatFieldDesign().Delimiter.ToChar(), Is.EqualTo(',')); //既定
                Assert.That(CsvDelimiterKind.Comma.ToChar(), Is.EqualTo(','));
                Assert.That(CsvDelimiterKind.Tab.ToChar(), Is.EqualTo('\t'));
                Assert.That(CsvDelimiterKind.Semicolon.ToChar(), Is.EqualTo(';'));
            });
        }

        [Test]
        public void BulkDownloadExtensionFallsBackToCsv()
        {
            //本体クライアントは IBulkFileTransferFieldDesign.Extension でダウンロード拡張子を決める
            Assert.Multiple(() =>
            {
                Assert.That(((IBulkFileTransferFieldDesign)new CsvFileFormatFieldDesign()).Extension, Is.EqualTo("csv"));
                Assert.That(((IBulkFileTransferFieldDesign)new CsvFileFormatFieldDesign { FileExtension = "" }).Extension, Is.EqualTo("csv"));
                Assert.That(((IBulkFileTransferFieldDesign)new CsvFileFormatFieldDesign { FileExtension = "txt" }).Extension, Is.EqualTo("txt"));
            });
        }
    }
}
