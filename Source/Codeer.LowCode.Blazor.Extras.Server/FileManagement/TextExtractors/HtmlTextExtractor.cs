using Codeer.LowCode.Blazor.Extras.SemanticSearch;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    /// <summary>HTML (.html .htm): 文字の符号化はテキスト (<see cref="PlainTextExtractor"/>) と同じに読み、タグを除いて文字参照を戻す (リッチテキストの値と同じやり方)。</summary>
    public class HtmlTextExtractor : IFileTextExtractor
    {
        /// <inheritdoc/>
        public IEnumerable<string> Extensions => [".html", ".htm"];

        /// <inheritdoc/>
        public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var memory = await ExtractorStream.ToMemoryAsync(content, cancellationToken);
            return SemanticSearchText.StripTags(PlainTextExtractor.Decode(memory.GetBuffer(), (int)memory.Length));
        }
    }
}
