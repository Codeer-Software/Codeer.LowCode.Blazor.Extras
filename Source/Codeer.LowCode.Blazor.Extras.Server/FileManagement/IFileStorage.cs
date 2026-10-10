namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// FileField の実体ファイルの置き場所 1 つ (名前付き)。ファイルは Guid をキーにフラットに置く。
    /// 組み込みは <see cref="FileSystemFileStorage"/> / <see cref="AzureBlobFileStorage"/> / <see cref="S3FileStorage"/>。
    /// 独自の置き場所はこれを実装し、アプリの対応表 (テンプレートの FileStorageTable) に足す。
    /// </summary>
    public interface IFileStorage
    {
        /// <summary>FileField の StorageName と突き合わせる名前。</summary>
        string Name { get; }
        Task<MemoryStream> ReadAsync(Guid file);
        Task WriteAsync(Guid file, MemoryStream content);
        Task DeleteAsync(Guid file);

        /// <summary>
        /// 読みながら返すための Stream (読み取り専用・シーク可能。読み終えた側が閉じる)。
        /// 動画の再生のようにブラウザが Range で部分を要求する用途で、全部をメモリに置かずに済ませる (<see cref="StorageAccess.OpenReadAsync(IEnumerable{IFileStorage}, DataIO.FileLocation)"/>)。
        /// 既定は <see cref="ReadAsync"/> (全部読んでから返す) なので、独自の置き場所は必要なときだけ部分読みで実装する (位置指定でしか読めない置き場所は <see cref="RangeReadStream"/>)。
        /// </summary>
        async Task<Stream> OpenReadAsync(Guid file) => await ReadAsync(file);

        /// <summary>
        /// 読みながら書く (全部をメモリに置かない)。アップロードの本文のように、長さが分からず戻れない Stream も受ける
        /// (<see cref="StorageAccess.WriteFile(IEnumerable{IFileStorage}, string?, Guid, Stream)"/>)。
        /// 既定は <see cref="WriteAsync(Guid, MemoryStream)"/> (全部読んでから書く) なので、独自の置き場所は必要なときだけ実装する。
        /// </summary>
        async Task WriteAsync(Guid file, Stream content)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory);
            memory.Position = 0;
            await WriteAsync(file, memory);
        }
    }

    public static class FileStorageExtensions
    {
        public static IFileStorage Find(this IEnumerable<IFileStorage> storages, string? name)
            => storages.FirstOrDefault(e => e.Name == name)
               ?? throw LowCodeException.Create($"{name} Invalid storage name");
    }
}
