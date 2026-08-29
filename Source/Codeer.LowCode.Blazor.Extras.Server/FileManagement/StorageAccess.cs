using Codeer.LowCode.Blazor.DataIO;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>FileField の実体ファイルの読み書き。置き場所は名前で <see cref="IFileStorage"/> を選ぶ。簡易形式 (<see cref="FileStorage"/>[]) のオーバーロードも同じ経路に流れる。</summary>
    public class StorageAccess
    {
        public static async Task<MemoryStream> ReadFileAsync(IEnumerable<IFileStorage> storages, FileLocation file)
            => await storages.Find(file.StorageName).ReadAsync(file.Guid);

        public static async Task DeleteFiles(IEnumerable<IFileStorage> storages, string storageName, Guid[] files)
        {
            var storage = storages.Find(storageName);
            foreach (var file in files)
            {
                try { await storage.DeleteAsync(file); }
                catch { }
            }
        }

        public static async Task WriteFile(IEnumerable<IFileStorage> storages, string? storageName, Guid guid, MemoryStream memoryStream)
            => await storages.Find(storageName).WriteAsync(guid, memoryStream);

        public static Task<MemoryStream> ReadFileAsync(FileStorage[] storages, FileLocation file)
            => ReadFileAsync(storages.ToFileStorages(), file);

        public static Task DeleteFiles(FileStorage[] storages, string storageName, Guid[] files)
            => DeleteFiles(storages.ToFileStorages(), storageName, files);

        public static Task WriteFile(FileStorage[] storages, string? storageName, Guid guid, MemoryStream memoryStream)
            => WriteFile(storages.ToFileStorages(), storageName, guid, memoryStream);
    }
}
