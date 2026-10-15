using DocumentFormat.OpenXml.Packaging;
using System.Text;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    /// <summary>PowerPoint (.pptx): スライド順に、図形の段落ごと。</summary>
    public class PowerPointTextExtractor : IFileTextExtractor
    {
        /// <inheritdoc/>
        public IEnumerable<string> Extensions => [".pptx"];

        /// <inheritdoc/>
        public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var memory = await ExtractorStream.ToMemoryAsync(content, cancellationToken);
            using var document = PresentationDocument.Open(memory, false);
            var presentationPart = document.PresentationPart;
            var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>();
            if (presentationPart == null || slideIds == null) return null;
            var sb = new StringBuilder();
            foreach (var slideId in slideIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (slideId.RelationshipId?.Value is not { } relationshipId || presentationPart.GetPartById(relationshipId) is not SlidePart slide) continue;
                foreach (var paragraph in slide.Slide?.Descendants<Drawing.Paragraph>() ?? [])
                {
                    var text = string.Concat(paragraph.Descendants<Drawing.Text>().Select(t => t.Text));
                    if (!string.IsNullOrWhiteSpace(text)) sb.Append(text).Append('\n');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
