using Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// 添付ファイルの中身を文字列にする対応表 (拡張子 → <see cref="IFileTextExtractor"/>)。意味検索 (SemanticSearchService.FileTextExtractors) が添付を索引に入れるときに使う。
    /// 拡張子は大文字小文字を区別しない。対応表に無い拡張子の添付は読まない (ファイル名だけ)。
    /// 既定 (<see cref="Default"/>) は PDF / Word (.docx) / PowerPoint (.pptx) / Excel (.xlsx) / テキスト (.txt .csv .md .json .xml) / HTML (.html .htm)。
    /// 旧 Office (.doc / .xls)・メール (.eml / .msg)・画像の OCR は入れていない (外部のサービスに送る実装はホストが差し込む)。
    /// <code>
    /// var extractors = FileTextExtractors.Default();
    /// extractors.Add(new MyMailTextExtractor()); //IFileTextExtractor の実装 (Extensions = [".msg"])
    /// extractors.Remove(".xlsx");
    /// </code>
    /// </summary>
    public sealed class FileTextExtractors
    {
        readonly Dictionary<string, IFileTextExtractor> _extractors = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>実装を登録する (実装の <see cref="IFileTextExtractor.Extensions"/> の全部)。同じ拡張子がすでにあれば、後から足したものが勝つ。</summary>
        public void Add(IFileTextExtractor extractor)
        {
            ArgumentNullException.ThrowIfNull(extractor);
            foreach (var extension in extractor.Extensions) _extractors[Normalize(extension)] = extractor;
        }

        /// <summary>拡張子を外す (その拡張子だけ。同じ実装の他の拡張子はそのまま)。外したら true。</summary>
        public bool Remove(string extension) => _extractors.Remove(Normalize(extension));

        /// <summary>登録されている拡張子。</summary>
        public IEnumerable<string> Extensions => _extractors.Keys.ToList();

        /// <summary>ファイル名の拡張子に対応する実装。無ければ null (呼び出し側はファイルを開かない)。</summary>
        public IFileTextExtractor? Find(string? fileName)
        {
            var extension = Path.GetExtension(fileName);
            return string.IsNullOrEmpty(extension) ? null : _extractors.GetValueOrDefault(Normalize(extension));
        }

        static string Normalize(string extension)
        {
            var e = (extension ?? string.Empty).Trim().ToLowerInvariant();
            return e.StartsWith('.') ? e : "." + e;
        }

        /// <summary>既定の対応表 (呼ぶたびに新しく作る。ホストは足したり外したりしてから SemanticSearchService.FileTextExtractors に渡す)。</summary>
        public static FileTextExtractors Default()
        {
            var extractors = new FileTextExtractors();
            extractors.Add(new PdfTextExtractor());
            extractors.Add(new WordTextExtractor());
            extractors.Add(new PowerPointTextExtractor());
            extractors.Add(new ExcelTextExtractor());
            extractors.Add(new PlainTextExtractor());
            extractors.Add(new HtmlTextExtractor());
            return extractors;
        }
    }
}
