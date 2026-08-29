using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;

namespace Codeer.LowCode.Blazor.Extras.Test.FileStorage
{
    /// <summary>StorageAccess → IFileStorage の経路。FileSystem で往復し、独自ストレージの差し込み・簡易形式 FileStorage[] の互換・S3 設定の写像を検証する。</summary>
    public class StorageAccessTest
    {
        string _dir = string.Empty;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "clb_fs_" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        List<IFileStorage> Storages() => [new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir })];

        [Test]
        public async Task FileSystemで書いて読んで消す()
        {
            var guid = Guid.NewGuid();
            var storages = Storages();
            await StorageAccess.WriteFile(storages, "Local", guid, new MemoryStream([1, 2, 3]));
            Assert.That(File.Exists(Path.Combine(_dir, guid.ToString())), Is.True);

            var read = await StorageAccess.ReadFileAsync(storages, new FileLocation { StorageName = "Local", Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

            await StorageAccess.DeleteFiles(storages, "Local", [guid]);
            Assert.That(File.Exists(Path.Combine(_dir, guid.ToString())), Is.False);
        }

        [Test]
        public void 未知のストレージ名は例外()
        {
            Assert.ThrowsAsync<LowCodeException>(() => StorageAccess.WriteFile(Storages(), "Nope", Guid.NewGuid(), new MemoryStream()));
        }

        [Test]
        public async Task 削除は個別の失敗を握りつぶして続行する()
        {
            var storages = Storages();
            var a = Guid.NewGuid();
            await StorageAccess.WriteFile(storages, "Local", a, new MemoryStream([9]));
            await StorageAccess.DeleteFiles(storages, "Local", [Guid.NewGuid(), a]);
            Assert.That(File.Exists(Path.Combine(_dir, a.ToString())), Is.False);
        }

        class InMemory : IFileStorage
        {
            public readonly Dictionary<Guid, byte[]> Files = new();
            public string Name => "Mem";
            public Task<MemoryStream> ReadAsync(Guid file) => Task.FromResult(new MemoryStream(Files[file]));
            public Task WriteAsync(Guid file, MemoryStream content) { Files[file] = content.ToArray(); return Task.CompletedTask; }
            public Task DeleteAsync(Guid file) { Files.Remove(file); return Task.CompletedTask; }
        }

        [Test]
        public async Task 独自ストレージを並べれば名前で選ばれる()
        {
            var mem = new InMemory();
            var storages = Storages().Append(mem).ToList();
            var guid = Guid.NewGuid();
            await StorageAccess.WriteFile(storages, "Mem", guid, new MemoryStream([7]));
            Assert.That(mem.Files[guid], Is.EqualTo(new byte[] { 7 }));
            Assert.That(Directory.Exists(_dir), Is.False, "FileSystem 側には書かれない");
            var read = await StorageAccess.ReadFileAsync(storages, new FileLocation { StorageName = "Mem", Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(new byte[] { 7 }));
        }

        [Test]
        public async Task 簡易形式のFileStorage配列でも動く()
        {
            Server.FileManagement.FileStorage[] legacy = [new() { Name = "Local", FileStorageType = FileStorageType.FileSystem, Directory = _dir }];
            var guid = Guid.NewGuid();
            await StorageAccess.WriteFile(legacy, "Local", guid, new MemoryStream([4]));
            var read = await StorageAccess.ReadFileAsync(legacy, new FileLocation { StorageName = "Local", Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(new byte[] { 4 }));

            var converted = new Server.FileManagement.FileStorage { Name = "Az", FileStorageType = FileStorageType.AzureBlobStorage, ContainerName = "c", ConnectionString = "UseDevelopmentStorage=true" }.ToFileStorage();
            Assert.That(converted, Is.InstanceOf<AzureBlobFileStorage>());
            Assert.That(converted.Name, Is.EqualTo("Az"));
        }

        [Test]
        public void Azure設定は接続文字列かURIのどちらかとコンテナ名が必須()
        {
            var byUri = new AzureBlobFileStorage(new AzureBlobStorageSettings { Name = "Az", BlobServiceUri = "https://acct.blob.core.windows.net", ContainerName = "files" });
            Assert.That(byUri.Name, Is.EqualTo("Az"));
            var byCs = new AzureBlobFileStorage(new AzureBlobStorageSettings { Name = "Az2", ConnectionString = "UseDevelopmentStorage=true", ContainerName = "files" });
            Assert.That(byCs.Name, Is.EqualTo("Az2"));
            Assert.Throws<LowCodeException>(() => new AzureBlobFileStorage(new AzureBlobStorageSettings { Name = "Az", ContainerName = "files" }));
            Assert.Throws<LowCodeException>(() => new AzureBlobFileStorage(new AzureBlobStorageSettings { Name = "Az", BlobServiceUri = "https://acct.blob.core.windows.net" }));
        }

        [Test]
        public void S3設定の写像_リージョン指定()
        {
            var config = S3FileStorage.CreateConfig(new() { Region = "ap-northeast-1" });
            Assert.That(config.RegionEndpoint?.SystemName, Is.EqualTo("ap-northeast-1"));
            Assert.That(config.ForcePathStyle, Is.False);
        }

        [Test]
        public void S3設定の写像_互換エンドポイント()
        {
            var config = S3FileStorage.CreateConfig(new() { ServiceUrl = "http://localhost:9000", Region = "us-east-1" });
            Assert.That(config.ServiceURL, Does.StartWith("http://localhost:9000")); // SDK が末尾に / を補う
            Assert.That(config.ForcePathStyle, Is.True);
            Assert.That(config.AuthenticationRegion, Is.EqualTo("us-east-1"));
        }

        [Test]
        public void S3資格情報_プロファイル未指定なら既定チェーン_存在しないプロファイルは例外()
        {
            Assert.That(S3FileStorage.CreateCredentials(new()), Is.Null);
            Assert.Throws<LowCodeException>(() => S3FileStorage.CreateCredentials(new() { ProfileName = "clb-no-such-profile-" + Guid.NewGuid().ToString("N") }));
        }

        [Test]
        public void S3キーは接頭辞付き()
        {
            var g = Guid.NewGuid();
            Assert.That(S3FileStorage.KeyOf(new() { KeyPrefix = "attachments/" }, g), Is.EqualTo("attachments/" + g));
            Assert.That(S3FileStorage.KeyOf(new(), g), Is.EqualTo(g.ToString()));
            Assert.That(new S3FileStorage(new() { Name = "S3" }).Name, Is.EqualTo("S3"));
        }
    }
}
