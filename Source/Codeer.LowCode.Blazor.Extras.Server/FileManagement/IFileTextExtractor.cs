namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// 添付ファイルの中身を文字列にする実装 (<see cref="FileTextExtractors"/> に <see cref="FileTextExtractors.Add"/> で登録する)。
    /// 1 つの実装が複数の拡張子を受けてよい。
    /// </summary>
    public interface IFileTextExtractor
    {
        /// <summary>受ける拡張子 (小文字・ドット付き。".pdf")。</summary>
        IEnumerable<string> Extensions { get; }

        /// <summary>
        /// 添付の中身を文字列にする。null を返すとその添付は文字列にしない (ファイル名だけ)。<paramref name="content"/> は呼び出し側が閉じる。
        /// </summary>
        /// <param name="fileName">元のファイル名</param>
        /// <param name="content">ファイルの中身 (読み取り専用)</param>
        /// <param name="cancellationToken">中断</param>
        Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken);
    }
}
