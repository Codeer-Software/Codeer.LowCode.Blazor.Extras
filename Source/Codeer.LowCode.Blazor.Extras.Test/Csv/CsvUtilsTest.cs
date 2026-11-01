using Codeer.LowCode.Blazor.Extras.Csv;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Csv;
using Codeer.LowCode.Blazor.Repository.Design;
using Excel.Report.PDF;
using System.Text;

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

        //式インジェクション対策 (CsvFileFormatFieldDesign.PreventFormulaInjection。出力で ' を前置、取込で外す。既定はオフ)

        static readonly List<string> FormulaLikeCells =
            ["=1+1", "+cmd|' /C calc'!A0", "-2+3", "@SUM(A1)", "\tx", "\rx", "-123", "+1.5", "-1,234.5", "-", "'引用", "plain", ""];

        [Test]
        public void FormulaLikeCellsArePrefixedWithApostropheOnWrite()
        {
            using var ms = CsvUtils.CreateCsvBinary([FormulaLikeCells], CsvEncodingKind.Utf8, '\t', preventFormulaInjection: true);
            //取込側の ' 外しを通さず、書かれたセルをそのまま見る
            var cells = CsvTextParser.Parse(new StringReader(new StreamReader(ms).ReadToEnd()), '\t')[0];
            Assert.That(cells, Is.EqualTo(new[]
            {
                "'=1+1", "'+cmd|' /C calc'!A0", "'-2+3", "'@SUM(A1)", "'\tx", "'\rx",
                "-123", "+1.5", "-1,234.5", "-",   //数値と "-" 1 文字はそのまま
                "''引用",                          //' で始まる値は ' を 1 つ足す (取込で 1 つ外れて元に戻る)
                "plain", "",
            }));
        }

        [Test]
        public void FormulaLikeCellsRoundTrip()
        {
            using var ms = CsvUtils.CreateCsvBinary([FormulaLikeCells], CsvEncodingKind.Utf8Bom, preventFormulaInjection: true);
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.Utf8Bom, preventFormulaInjection: true);
            Assert.That(rows, Is.EqualTo(new[] { FormulaLikeCells }));
        }

        [Test]
        public void DefaultWritesAndReadsCellsAsIs()
        {
            //既定 (対策オフ) はデータを加工しない (式を意図して出し入れする用途・相手仕様のファイル)
            List<List<string>> texts = [["+81-3-1234-5678", "-A01", "'x", "=1+1"]];
            using var ms = CsvUtils.CreateCsvBinary(texts, CsvEncodingKind.Utf8);
            var raw = CsvTextParser.Parse(new StringReader(new StreamReader(ms).ReadToEnd()), ',')[0];
            Assert.That(raw, Is.EqualTo(texts[0]));
            ms.Position = 0;
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.Utf8);
            Assert.That(rows, Is.EqualTo(texts));
        }

        [Test]
        public void ReadStripsOneLeadingApostrophe()
        {
            //Excel で開いて保存し直した CSV (Excel は先頭の ' を文字として保持する)。' は 1 つだけ外す
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes("'=abc,''x,',y'\r\n"));
            var rows = CsvUtils.ReadAllTextsFromCsv(ms, CsvEncodingKind.Utf8, preventFormulaInjection: true);
            Assert.That(rows, Is.EqualTo(new[] { new[] { "=abc", "'x", "", "y'" } }));
        }

        [Test]
        public async Task ReadStripsLeadingApostropheFromExcelSavedFile()
        {
            //ダウンロードした CSV を Excel で xlsx として保存し直してアップロードした形
            List<List<string>> texts = [["Name.Value"], ["'=1+1"]];
            using var xlsx = ExcelUtils.CreateExcelBinary(texts, "data");
            var rows = await CsvUtils.ReadAllTextsFromFileBinary(xlsx, CsvEncodingKind.Utf8Bom, preventFormulaInjection: true);
            Assert.That(rows, Is.EqualTo(new[] { new[] { "Name.Value" }, new[] { "=1+1" } }));
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
