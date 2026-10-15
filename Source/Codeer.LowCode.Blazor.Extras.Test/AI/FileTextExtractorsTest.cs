using ClosedXML.Excel;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Drawing = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// FileTextExtractors (意味検索が添付の中身を文章にする対応表) と既定の IFileTextExtractor: 既定の各種類の取り出し方と、対応表の出し入れ。
    /// docx / pptx / xlsx / pdf はテストの中で作る。
    /// </summary>
    public class FileTextExtractorsTest
    {
        static FileTextExtractorsTest() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        static async Task<string?> ExtractAsync(string fileName, byte[] content)
        {
            var extractor = FileTextExtractors.Default().Find(fileName);
            Assert.That(extractor, Is.Not.Null, fileName);
            using var stream = new MemoryStream(content, false);
            return await extractor!.ExtractAsync(fileName, stream, CancellationToken.None);
        }

        static string Lines(string? text) => string.Join("\n", (text ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0));

        [Test]
        public void 既定の対応表の拡張子()
        {
            Assert.That(FileTextExtractors.Default().Extensions.OrderBy(e => e),
                Is.EqualTo(new[] { ".csv", ".docx", ".htm", ".html", ".json", ".md", ".pdf", ".pptx", ".txt", ".xlsx", ".xml" }));
        }

        [Test]
        public void 対応表に無い拡張子は何もしない_大文字小文字は問わない()
        {
            var extractors = FileTextExtractors.Default();
            Assert.That(extractors.Find("old.doc"), Is.Null);
            Assert.That(extractors.Find("book.xls"), Is.Null);
            Assert.That(extractors.Find("mail.msg"), Is.Null);
            Assert.That(extractors.Find("README"), Is.Null, "拡張子なし");
            Assert.That(extractors.Find(null), Is.Null);
            Assert.That(extractors.Find("REPORT.PDF"), Is.InstanceOf<PdfTextExtractor>());
            Assert.That(extractors.Find("a.htm"), Is.SameAs(extractors.Find("a.html")), "1 つの実装が 2 つの拡張子を受ける");
            Assert.That(FileTextExtractors.Default().Find("a.pdf"), Is.Not.SameAs(extractors.Find("a.pdf")), "Default() は呼ぶたびに新しい");
        }

        [Test]
        public void Addは実装の拡張子を全部登録し_後から足したものが勝つ_Removeはその拡張子だけ外す()
        {
            var extractors = FileTextExtractors.Default();
            var mail = new FakeExtractor(".msg", "EML");
            extractors.Add(mail);
            Assert.That(extractors.Find("a.msg"), Is.SameAs(mail));
            Assert.That(extractors.Find("a.eml"), Is.SameAs(mail), "大文字の拡張子で宣言しても引ける");

            var text = new FakeExtractor(".txt");
            extractors.Add(text);
            Assert.That(extractors.Find("memo.txt"), Is.SameAs(text), "後勝ち");
            Assert.That(extractors.Find("a.csv"), Is.InstanceOf<PlainTextExtractor>(), "同じ拡張子だけ差し替わる");

            Assert.That(extractors.Remove(".MSG"), Is.True);
            Assert.That(extractors.Remove(".msg"), Is.False);
            Assert.That(extractors.Find("a.msg"), Is.Null);
            Assert.That(extractors.Find("a.eml"), Is.SameAs(mail), "同じ実装の他の拡張子はそのまま");
            Assert.That(extractors.Remove(".pdf"), Is.True);
            Assert.That(extractors.Find("a.pdf"), Is.Null);
            Assert.That(FileTextExtractors.Default().Find("a.pdf"), Is.Not.Null, "Default() は呼ぶたびに新しい");
        }

        [Test]
        public async Task 足した実装で取り出せる()
        {
            var extractors = new FileTextExtractors();
            extractors.Add(new FakeExtractor(".msg", ".eml"));
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("本文"), false);
            Assert.That(await extractors.Find("a.eml")!.ExtractAsync("a.eml", stream, CancellationToken.None), Is.EqualTo("a.eml: 本文"));
            Assert.That(extractors.Extensions.OrderBy(e => e), Is.EqualTo(new[] { ".eml", ".msg" }));
        }

        class FakeExtractor(params string[] extensions) : IFileTextExtractor
        {
            public IEnumerable<string> Extensions => extensions;

            public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
                => fileName + ": " + await new StreamReader(content).ReadToEndAsync(cancellationToken);
        }

        [Test]
        public async Task テキストはBOM付きUTF8とBOM無しUTF8とShift_JISを読む()
        {
            Assert.That(await ExtractAsync("a.txt", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("BOM 付き 日本語")]), Is.EqualTo("BOM 付き 日本語"));
            Assert.That(await ExtractAsync("a.csv", Encoding.UTF8.GetBytes("品名,数量\nねじ,10")), Is.EqualTo("品名,数量\nねじ,10"));
            Assert.That(await ExtractAsync("a.md", Encoding.GetEncoding("shift_jis").GetBytes("# 見積\n合計 ①")), Is.EqualTo("# 見積\n合計 ①"), "UTF-8 として読めなければ Shift_JIS");
            Assert.That(await ExtractAsync("a.json", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{\"名\":1}")]), Is.EqualTo("{\"名\":1}"), "UTF-16 の BOM");
        }

        [Test]
        public async Task HTMLはタグを除いて文字参照を戻す()
        {
            var text = await ExtractAsync("page.HTML", Encoding.UTF8.GetBytes("<html><body><h1>納期</h1><p>遅れ&amp;欠品</p></body></html>"));
            Assert.That(Lines(text).Replace(" ", ""), Is.EqualTo("納期遅れ&欠品"));
            Assert.That(text, Does.Not.Contain("<"));
        }

        [Test]
        public async Task Wordは本文の段落と表を順に取り出す()
        {
            using var memory = new MemoryStream();
            using (var document = WordprocessingDocument.Create(memory, WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                main.Document = new Word.Document(new Word.Body(
                    Paragraph("見積書"),
                    new Word.Table(
                        new Word.TableRow(Cell("品名"), Cell("数量")),
                        new Word.TableRow(Cell("ねじ"), Cell("10"))),
                    Paragraph("以上")));
            }
            Assert.That(Lines(await ExtractAsync("quote.docx", memory.ToArray())), Is.EqualTo("見積書\n品名\t数量\nねじ\t10\n以上"));

            static Word.Paragraph Paragraph(string text) => new(new Word.Run(new Word.Text(text)));
            static Word.TableCell Cell(string text) => new(Paragraph(text));
        }

        [Test]
        public async Task PowerPointはスライド順に取り出す()
        {
            using var memory = new MemoryStream();
            using (var document = PresentationDocument.Create(memory, PresentationDocumentType.Presentation))
            {
                var presentation = document.AddPresentationPart();
                presentation.Presentation = new P.Presentation();
                var slideIds = new P.SlideIdList();
                uint id = 256;
                //関係の追加順と表示順を逆にして、スライド一覧の順で読むことを確かめる
                var second = AddSlide(presentation, "2 枚目の本文");
                var first = AddSlide(presentation, "表紙", "副題");
                slideIds.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(first) });
                slideIds.Append(new P.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(second) });
                presentation.Presentation.Append(slideIds);
            }
            Assert.That(Lines(await ExtractAsync("deck.pptx", memory.ToArray())), Is.EqualTo("表紙\n副題\n2 枚目の本文"));

            static SlidePart AddSlide(PresentationPart presentation, params string[] paragraphs)
            {
                var slide = presentation.AddNewPart<SlidePart>();
                var body = new P.TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle());
                foreach (var p in paragraphs) body.Append(new Drawing.Paragraph(new Drawing.Run(new Drawing.Text(p))));
                slide.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1, Name = "" }, new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                    new P.GroupShapeProperties(),
                    new P.Shape(new P.NonVisualShapeProperties(new P.NonVisualDrawingProperties { Id = 2, Name = "Text" }, new P.NonVisualShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()),
                        new P.ShapeProperties(), body))));
                return slide;
            }
        }

        [Test]
        public async Task Excelはシート名と使用範囲の行をタブ区切りで_数式はキャッシュ値()
        {
            using var memory = new MemoryStream();
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.AddWorksheet("見積");
                sheet.Cell("A1").Value = "品名";
                sheet.Cell("B1").Value = "単価";
                sheet.Cell("C1").Value = "金額";
                sheet.Cell("A2").Value = "ねじ";
                sheet.Cell("B2").Value = 12.5;
                sheet.Cell("C2").FormulaA1 = "B2*4";
                sheet.Cell("A3").Value = new DateTime(2026, 10, 15);
                workbook.AddWorksheet("空");
                workbook.SaveAs(memory, new SaveOptions { EvaluateFormulasBeforeSaving = true });
            }
            Assert.That(Lines(await ExtractAsync("book.xlsx", memory.ToArray())), Is.EqualTo("見積\n品名\t単価\t金額\nねじ\t12.5\t50\n2026-10-15\n空"));
        }

        [Test]
        public async Task PDFはページ順に取り出す()
        {
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            builder.AddPage(PageSize.A4).AddText("Quotation for bolts", 12, new PdfPoint(50, 700), font);
            builder.AddPage(PageSize.A4).AddText("Delivery in three weeks", 12, new PdfPoint(50, 700), font);
            var text = Lines(await ExtractAsync("quote.pdf", builder.Build()));
            Assert.That(text, Does.Contain("Quotation for bolts").And.Contain("Delivery in three weeks"));
            Assert.That(text.IndexOf("Quotation", StringComparison.Ordinal), Is.LessThan(text.IndexOf("Delivery", StringComparison.Ordinal)));
        }
    }
}
