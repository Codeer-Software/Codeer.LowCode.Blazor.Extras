using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;

namespace Codeer.LowCode.Blazor.Extras.Test.FileStorage
{
    /// <summary>
    /// 実 Azure Blob に対する接続文字列経路の往復。旧設定 (FileStorage[] 簡易形式) と新設定 (AzureBlobStorageSettings.ConnectionString) の両方。
    /// 接続文字列は環境変数 CLB_AZURE_TEST_CONNECTION_STRING から取る (無ければ Ignore)。Entra の資格情報は使わない。
    /// </summary>
    [Category("Azure")]
    public class AzureBlobConnectionStringIntegrationTest
    {
        static string ConnectionString => Environment.GetEnvironmentVariable("CLB_AZURE_TEST_CONNECTION_STRING") ?? string.Empty;
        static string Container => Environment.GetEnvironmentVariable("CLB_AZURE_TEST_CONTAINER") ?? "formsfiles";

        static async Task RoundTrip(IEnumerable<IFileStorage> storages, string name)
        {
            var guid = Guid.NewGuid();
            var payload = new byte[] { 9, 8, 7 };
            await StorageAccess.WriteFile(storages, name, guid, new MemoryStream(payload));
            var read = await StorageAccess.ReadFileAsync(storages, new FileLocation { StorageName = name, Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(payload));
            await StorageAccess.DeleteFiles(storages, name, [guid]);
            Assert.CatchAsync<Azure.RequestFailedException>(() => storages.Find(name).ReadAsync(guid));
        }

        [Test]
        public async Task 旧設定_FileStorage配列_接続文字列で往復()
        {
            if (string.IsNullOrEmpty(ConnectionString)) Assert.Ignore("CLB_AZURE_TEST_CONNECTION_STRING is not set");
            Server.FileManagement.FileStorage[] legacy =
            [
                new() { Name = "Azure", FileStorageType = FileStorageType.AzureBlobStorage, ContainerName = Container, ConnectionString = ConnectionString },
            ];
            //旧アプリと同じ FileStorage[] オーバーロードを通す
            var guid = Guid.NewGuid();
            await StorageAccess.WriteFile(legacy, "Azure", guid, new MemoryStream([1]));
            var read = await StorageAccess.ReadFileAsync(legacy, new FileLocation { StorageName = "Azure", Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(new byte[] { 1 }));
            await StorageAccess.DeleteFiles(legacy, "Azure", [guid]);
            Assert.CatchAsync<Azure.RequestFailedException>(() => legacy.ToFileStorages().Find("Azure").ReadAsync(guid));
        }

        [Test]
        public async Task 新設定_AzureBlobStorageSettings_接続文字列で往復()
        {
            if (string.IsNullOrEmpty(ConnectionString)) Assert.Ignore("CLB_AZURE_TEST_CONNECTION_STRING is not set");
            var storages = new List<IFileStorage>
            {
                new AzureBlobFileStorage(new AzureBlobStorageSettings { Name = "Azure", ContainerName = Container, ConnectionString = ConnectionString }),
            };
            await RoundTrip(storages, "Azure");
        }
    }
}
