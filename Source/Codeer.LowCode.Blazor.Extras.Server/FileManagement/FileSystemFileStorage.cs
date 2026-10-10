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

        //再生中 (応答がブラウザの読み進めに合わせて長く開いたまま) でも差し替え・削除 (DeleteAsync) を止めないよう FileShare.Delete を付ける
        public Task<Stream> OpenReadAsync(Guid file)
            => Task.FromResult<Stream>(new FileStream(PathOf(file), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan));

        public async Task WriteAsync(Guid file, MemoryStream content)
        {
            var path = PathOf(file);
            Directory.CreateDirectory(_settings.Directory);
            await File.WriteAllBytesAsync(path, content.ToArray());
        }

        //読みながらファイルへ書く (アップロードの本文をメモリに置かない)
        public async Task WriteAsync(Guid file, Stream content)
        {
            Directory.CreateDirectory(_settings.Directory);
            await using var fileStream = new FileStream(PathOf(file), FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await content.CopyToAsync(fileStream);
        }

        public Task DeleteAsync(Guid file)
        {
            File.Delete(PathOf(file));
            return Task.CompletedTask;
        }
    }
}
