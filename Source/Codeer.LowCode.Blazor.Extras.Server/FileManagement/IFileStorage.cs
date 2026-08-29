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
    }

    public static class FileStorageExtensions
    {
        public static IFileStorage Find(this IEnumerable<IFileStorage> storages, string? name)
            => storages.FirstOrDefault(e => e.Name == name)
               ?? throw LowCodeException.Create($"{name} Invalid storage name");
    }
}
