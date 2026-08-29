using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;

namespace Codeer.LowCode.Blazor.Extras.Test.FileStorage
{
    /// <summary>
    /// 実 Azure Blob に対する往復 (推奨形式 = DefaultAzureCredential)。資格情報は環境変数
    /// AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET (サービスプリンシパル) か Azure CLI ログインに任せる。
    /// 環境変数が無い環境では Ignore。接続先は CLB_AZURE_TEST_BLOB_URI / CLB_AZURE_TEST_CONTAINER で差し替え可。
    /// </summary>
    [Category("Azure")]
    public class AzureBlobStorageIntegrationTest
    {
        [Test]
        public async Task 実AzureBlobで書いて読んで消す()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")))
                Assert.Ignore("Azure credentials are not configured in this environment");

            var settings = new AzureBlobStorageSettings
            {
                Name = "Azure",
                BlobServiceUri = Environment.GetEnvironmentVariable("CLB_AZURE_TEST_BLOB_URI") ?? "https://codeerformsblobs.blob.core.windows.net",
                ContainerName = Environment.GetEnvironmentVariable("CLB_AZURE_TEST_CONTAINER") ?? "formsfiles",
            };
            var storages = new List<IFileStorage> { new AzureBlobFileStorage(settings) };
            var guid = Guid.NewGuid();
            var payload = new byte[] { 1, 2, 3, 4, 5 };

            await StorageAccess.WriteFile(storages, "Azure", guid, new MemoryStream(payload));
            var read = await StorageAccess.ReadFileAsync(storages, new FileLocation { StorageName = "Azure", Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(payload));

            await StorageAccess.DeleteFiles(storages, "Azure", [guid]);
            Assert.CatchAsync<Azure.RequestFailedException>(() => storages.Find("Azure").ReadAsync(guid), "削除後は読めない (BlobNotFound)");
        }
    }
}
