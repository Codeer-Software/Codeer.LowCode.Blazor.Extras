using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    /// <summary>テキスト (.txt .csv .md .json .xml): BOM があればその符号化、無ければ UTF-8 で厳密に読み、読めなければ Shift_JIS。</summary>
    public class PlainTextExtractor : IFileTextExtractor
    {
        static PlainTextExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); //Shift_JIS 用

        /// <inheritdoc/>
        public IEnumerable<string> Extensions => [".txt", ".csv", ".md", ".json", ".xml"];

        /// <inheritdoc/>
        public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var memory = await ExtractorStream.ToMemoryAsync(content, cancellationToken);
            return Decode(memory.GetBuffer(), (int)memory.Length);
        }

        internal static string Decode(byte[] bytes, int length)
        {
            if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, length - 3);
            if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, length - 2);
            if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes, 0, length);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding("shift_jis").GetString(bytes, 0, length);
            }
        }
    }
}
