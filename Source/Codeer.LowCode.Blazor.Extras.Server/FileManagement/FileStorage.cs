namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// 簡易形式: appsettings "FileStorages" 1 要素 (種別と設定を 1 クラスに持つ)。FileSystem / Azure Blob だけならこれで足りる。
    /// S3 や独自の保存先を使うアプリは種別別形式 (<see cref="FileSystemStorageSettings"/> / <see cref="AzureBlobStorageSettings"/> /
    /// <see cref="S3StorageSettings"/> を別セクションで持ち、<see cref="IFileStorage"/> の並びとして渡す) を使う。
    /// 内部では <see cref="ToFileStorage"/> で同じ <see cref="IFileStorage"/> に変換されるので、どちらでも動きは同じ。
    /// </summary>
    public class FileStorage
    {
        public FileStorageType FileStorageType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;

        public IFileStorage ToFileStorage() => FileStorageType switch
        {
            FileStorageType.FileSystem => new FileSystemFileStorage(new FileSystemStorageSettings { Name = Name, Directory = Directory }),
            FileStorageType.AzureBlobStorage => new AzureBlobFileStorage(Name, new Azure.Storage.Blobs.BlobContainerClient(ConnectionString, ContainerName)),
            _ => throw LowCodeException.Create("invalid storage type"),
        };
    }

    public static class FileStorageArrayExtensions
    {
        public static List<IFileStorage> ToFileStorages(this IEnumerable<FileStorage> storages) => storages.Select(e => e.ToFileStorage()).ToList();
    }
}
