using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;

namespace Codeer.LowCode.Blazor.Extras.Test.FileStorage
{
    /// <summary>
    /// 実 S3 に対する往復。資格情報は SDK 既定チェーン (環境変数 AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY 等) に任せる。
    /// 環境変数が無い環境では Ignore。バケット名は環境変数 CLB_S3_TEST_BUCKET / CLB_S3_TEST_REGION で差し替え可。
    /// </summary>
    [Category("S3")]
    public class S3StorageIntegrationTest
    {
        [Test]
        public async Task 実S3で書いて読んで消す()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")))
                Assert.Ignore("AWS credentials are not configured in this environment");

            var settings = new S3StorageSettings
            {
                Name = "S3",
                BucketName = Environment.GetEnvironmentVariable("CLB_S3_TEST_BUCKET") ?? "codeer-test-1-567674825565-ap-northeast-1-an",
                Region = Environment.GetEnvironmentVariable("CLB_S3_TEST_REGION") ?? "ap-northeast-1",
                KeyPrefix = "clb-test/",
            };
            var storages = new List<IFileStorage> { new S3FileStorage(settings) };
            var guid = Guid.NewGuid();
            var payload = new byte[] { 1, 2, 3, 4, 5 };

            await StorageAccess.WriteFile(storages, "S3", guid, new MemoryStream(payload));
            var read = await StorageAccess.ReadFileAsync(storages, new FileLocation { StorageName = "S3", Guid = guid });
            Assert.That(read.ToArray(), Is.EqualTo(payload));

            await StorageAccess.DeleteFiles(storages, "S3", [guid]);
            Assert.CatchAsync<Amazon.S3.AmazonS3Exception>(
                () => storages.Find("S3").ReadAsync(guid), "削除後は読めない (NoSuchKey)");
        }
    }
}
