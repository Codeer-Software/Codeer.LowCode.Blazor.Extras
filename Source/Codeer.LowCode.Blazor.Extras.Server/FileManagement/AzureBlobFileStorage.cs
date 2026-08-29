using Azure.Identity;
using Azure.Storage.Blobs;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// Azure Blob Storage (推奨形式)。接続文字列 (共有キー) は使わず、<c>DefaultAzureCredential</c> で認証する
    /// (環境変数 → Workload Identity → Managed Identity → Visual Studio / Azure CLI のログイン)。
    /// ストレージアカウントには「ストレージ BLOB データ共同作成者」ロールを付ける。
    /// appsettings のセクション名はアプリが決める (テンプレートの既定は "AzureBlobStorages")。
    /// 接続文字列で繋ぎたい場合は簡易形式 (<see cref="FileStorage"/>) を使う。
    /// </summary>
    public class AzureBlobStorageSettings
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>Blob サービスの URI (例 https://myaccount.blob.core.windows.net)。</summary>
        public string BlobServiceUri { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
    }

    public class AzureBlobFileStorage : IFileStorage
    {
        readonly BlobContainerClient _container;

        public AzureBlobFileStorage(AzureBlobStorageSettings settings)
        {
            if (string.IsNullOrEmpty(settings.BlobServiceUri)) throw LowCodeException.Create("invalid blob service uri");
            if (string.IsNullOrEmpty(settings.ContainerName)) throw LowCodeException.Create("invalid container name");
            Name = settings.Name;
            _container = new BlobServiceClient(new Uri(settings.BlobServiceUri), new DefaultAzureCredential())
                .GetBlobContainerClient(settings.ContainerName);
        }

        /// <summary>簡易形式 (接続文字列) 用。</summary>
        internal AzureBlobFileStorage(string name, BlobContainerClient container)
        {
            Name = name;
            _container = container;
        }

        public string Name { get; }

        public async Task<MemoryStream> ReadAsync(Guid file)
        {
            var memoryStream = new MemoryStream();
            await _container.GetBlobClient($"{file}").DownloadToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        public async Task WriteAsync(Guid file, MemoryStream content) => await _container.GetBlobClient($"{file}").UploadAsync(content, true);

        public async Task DeleteAsync(Guid file) => await _container.GetBlobClient($"{file}").DeleteIfExistsAsync();
    }
}
