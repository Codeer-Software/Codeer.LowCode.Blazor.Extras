using ClosedXML.Excel;
using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    /// <summary>Excel (.xlsx): シートごとにシート名の行 + 使用範囲の行 (セルはタブ区切り。数式はキャッシュ値)。</summary>
    public class ExcelTextExtractor : IFileTextExtractor
    {
        /// <inheritdoc/>
        public IEnumerable<string> Extensions => [".xlsx"];

        /// <inheritdoc/>
        public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        {
            using var memory = await ExtractorStream.ToMemoryAsync(content, cancellationToken);
            using var workbook = new XLWorkbook(memory);
            var sb = new StringBuilder();
            foreach (var sheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append(sheet.Name).Append('\n');
                var range = sheet.RangeUsed();
                if (range == null) continue;
                foreach (var row in range.Rows())
                {
                    var line = string.Join("\t", row.Cells().Select(c => CellText(c.CachedValue))).TrimEnd('\t');
                    if (line.Length > 0) sb.Append(line).Append('\n');
                }
            }
            return sb.ToString();
        }

        static string CellText(XLCellValue value)
        {
            if (value.IsBlank) return string.Empty;
            if (value.IsNumber) return value.GetNumber().ToString("G15", CultureInfo.InvariantCulture);
            if (value.IsDateTime)
            {
                var d = value.GetDateTime();
                return d.ToString(d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            if (value.IsBoolean) return value.GetBoolean() ? "TRUE" : "FALSE";
            if (value.IsTimeSpan) return value.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture);
            if (value.IsError) return string.Empty;
            //改行・タブはセルの区切りと紛れるので空白に
            return value.GetText().Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }
    }
}
