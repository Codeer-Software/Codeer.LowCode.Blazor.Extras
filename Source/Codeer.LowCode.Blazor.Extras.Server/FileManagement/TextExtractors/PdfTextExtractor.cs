using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    /// <summary>PDF (.pdf): ページ順に、ページ内は文字の並び順 (PdfPig)。</summary>
    public class PdfTextExtractor : IFileTextExtractor
    {
        /// <inheritdoc/>
        public IEnumerable<string> Extensions => [".pdf"];

        /// <inheritdoc/>
        public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var memory = await ExtractorStream.ToMemoryAsync(content, cancellationToken);
            using var document = PdfDocument.Open(memory);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append(ContentOrderTextExtractor.GetText(page)).Append('\n');
            }
            return sb.ToString();
        }
    }
}
