using Azure.Identity;
using Azure.Storage.Blobs;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// Azure Blob Storage。認証は 2 通り:
    /// <list type="bullet">
    /// <item><see cref="ConnectionString"/> を指定 → 接続文字列 (共有キー) で接続。DB と同じ運用にしたいとき</item>
    /// <item>空なら <see cref="BlobServiceUri"/> + <c>DefaultAzureCredential</c> (環境変数 → Workload Identity → Managed Identity → Visual Studio / Azure CLI のログイン)。
    /// ストレージアカウントに「ストレージ BLOB データ共同作成者」ロールを付ける。キーを持たないので本番はこちらを推奨</item>
    /// </list>
    /// appsettings のセクション名はアプリが決める (テンプレートの既定は "AzureBlobStorages"、接続文字列は ConnectionStrings:&lt;Name&gt; からも補われる)。
    /// </summary>
    public class AzureBlobStorageSettings
    {
        public string Name { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        /// <summary>接続文字列。指定すればこれで接続する (BlobServiceUri は不要)。</summary>
        public string ConnectionString { get; set; } = string.Empty;
        /// <summary>Blob サービスの URI (例 https://myaccount.blob.core.windows.net)。ConnectionString が空のとき DefaultAzureCredential で接続する。</summary>
        public string BlobServiceUri { get; set; } = string.Empty;
    }

    public class AzureBlobFileStorage : IFileStorage
    {
        readonly BlobContainerClient _container;

        public AzureBlobFileStorage(AzureBlobStorageSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ContainerName)) throw LowCodeException.Create("invalid container name");
            Name = settings.Name;
            if (!string.IsNullOrEmpty(settings.ConnectionString))
            {
                _container = new BlobContainerClient(settings.ConnectionString, settings.ContainerName);
            }
            else if (!string.IsNullOrEmpty(settings.BlobServiceUri))
            {
                _container = new BlobServiceClient(new Uri(settings.BlobServiceUri), new DefaultAzureCredential())
                    .GetBlobContainerClient(settings.ContainerName);
            }
            else throw LowCodeException.Create("ConnectionString or BlobServiceUri is required");
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
