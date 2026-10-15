using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using System.Text;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    /// <summary>Word (.docx): 本文の段落と表 (表は行ごとにセルをタブ区切り)。</summary>
    public class WordTextExtractor : IFileTextExtractor
    {
        /// <inheritdoc/>
        public IEnumerable<string> Extensions => [".docx"];

        /// <inheritdoc/>
        public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var memory = await ExtractorStream.ToMemoryAsync(content, cancellationToken);
            using var document = WordprocessingDocument.Open(memory, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body == null) return null;
            var sb = new StringBuilder();
            AppendWordBlocks(body, sb);
            return sb.ToString();
        }

        static void AppendWordBlocks(OpenXmlElement parent, StringBuilder sb)
        {
            foreach (var element in parent.ChildElements)
            {
                switch (element)
                {
                    case Word.Paragraph paragraph:
                        sb.Append(paragraph.InnerText).Append('\n');
                        break;
                    case Word.Table table:
                        foreach (var row in table.Elements<Word.TableRow>())
                            sb.Append(string.Join("\t", row.Elements<Word.TableCell>().Select(c => string.Join(" ", c.Descendants<Word.Paragraph>().Select(p => p.InnerText))))).Append('\n');
                        break;
                    default:
                        //コンテンツコントロール等の入れ物は中の段落・表をたどる
                        AppendWordBlocks(element, sb);
                        break;
                }
            }
        }
    }
}
