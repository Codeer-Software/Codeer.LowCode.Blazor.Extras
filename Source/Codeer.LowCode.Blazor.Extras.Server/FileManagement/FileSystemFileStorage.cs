namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>サーバーのフォルダに置く。appsettings のセクション名はアプリが決める (テンプレートの既定は "FileSystemStorages")。</summary>
    public class FileSystemStorageSettings
    {
        public string Name { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
    }

    public class FileSystemFileStorage : IFileStorage
    {
        readonly FileSystemStorageSettings _settings;
        public FileSystemFileStorage(FileSystemStorageSettings settings) => _settings = settings;

        public string Name => _settings.Name;

        string PathOf(Guid file)
        {
            if (string.IsNullOrEmpty(_settings.Directory)) throw LowCodeException.Create("invalid directory");
            return Path.Combine(_settings.Directory, file.ToString());
        }

        public async Task<MemoryStream> ReadAsync(Guid file) => new MemoryStream(await File.ReadAllBytesAsync(PathOf(file)));

        public async Task WriteAsync(Guid file, MemoryStream content)
        {
            var path = PathOf(file);
            Directory.CreateDirectory(_settings.Directory);
            await File.WriteAllBytesAsync(path, content.ToArray());
        }

        public Task DeleteAsync(Guid file)
        {
            File.Delete(PathOf(file));
            return Task.CompletedTask;
        }
    }
}
