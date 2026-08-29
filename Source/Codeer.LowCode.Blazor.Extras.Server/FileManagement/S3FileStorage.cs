using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>Amazon S3 / S3 互換 API (MinIO, Cloudflare R2, Wasabi 等)。appsettings のセクション名はアプリが決める (テンプレートの既定は "S3Storages")。</summary>
    public class S3StorageSettings
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>バケット名 (必須)。</summary>
        public string BucketName { get; set; } = string.Empty;
        /// <summary>リージョン (例 ap-northeast-1)。ServiceUrl 指定時は署名用リージョン (省略可)。</summary>
        public string Region { get; set; } = string.Empty;
        /// <summary>S3 互換ストレージのエンドポイント (例 http://localhost:9000)。指定するとパス形式アドレッシングになる。AWS 本体では空。</summary>
        public string ServiceUrl { get; set; } = string.Empty;
        /// <summary>
        /// 使う共有プロファイル名 (~/.aws/credentials)。空なら AWS SDK 既定のクレデンシャル解決
        /// (環境変数 AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY → 既定プロファイル → ECS/EC2 の IAM ロール・IRSA → SSO)。
        /// アクセスキーを appsettings に書く設定は意図的に用意していない。本番は IAM ロール、ローカルは環境変数かプロファイルで。
        /// </summary>
        public string ProfileName { get; set; } = string.Empty;
        /// <summary>オブジェクトキーの接頭辞 (例 "attachments/")。1 バケットを複数用途で共有するとき用。</summary>
        public string KeyPrefix { get; set; } = string.Empty;
    }

    public class S3FileStorage : IFileStorage
    {
        readonly S3StorageSettings _settings;
        public S3FileStorage(S3StorageSettings settings) => _settings = settings;

        public string Name => _settings.Name;

        internal static AmazonS3Config CreateConfig(S3StorageSettings settings)
        {
            var config = new AmazonS3Config();
            if (!string.IsNullOrEmpty(settings.ServiceUrl))
            {
                //S3 互換ストレージは仮想ホスト形式 (bucket.host) を解決できないことが多いのでパス形式にする
                config.ServiceURL = settings.ServiceUrl;
                config.ForcePathStyle = true;
                if (!string.IsNullOrEmpty(settings.Region)) config.AuthenticationRegion = settings.Region;
            }
            else if (!string.IsNullOrEmpty(settings.Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
            }
            return config;
        }

        internal static AWSCredentials? CreateCredentials(S3StorageSettings settings)
        {
            if (string.IsNullOrEmpty(settings.ProfileName)) return null;
            if (new CredentialProfileStoreChain().TryGetAWSCredentials(settings.ProfileName, out var credentials)) return credentials;
            throw LowCodeException.Create($"AWS profile '{settings.ProfileName}' not found");
        }

        internal static string KeyOf(S3StorageSettings settings, Guid file) => $"{settings.KeyPrefix}{file}";

        IAmazonS3 CreateClient()
        {
            if (string.IsNullOrEmpty(_settings.BucketName)) throw LowCodeException.Create("invalid bucket name");
            var config = CreateConfig(_settings);
            var credentials = CreateCredentials(_settings);
            //ProfileName 未指定なら SDK 既定のクレデンシャルチェーン
            return credentials == null ? new AmazonS3Client(config) : new AmazonS3Client(credentials, config);
        }

        public async Task<MemoryStream> ReadAsync(Guid file)
        {
            using var client = CreateClient();
            using var response = await client.GetObjectAsync(_settings.BucketName, KeyOf(_settings, file));
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;
            return memoryStream;
        }

        public async Task WriteAsync(Guid file, MemoryStream content)
        {
            using var client = CreateClient();
            content.Position = 0;
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = KeyOf(_settings, file),
                InputStream = content,
                AutoCloseStream = false,
            });
        }

        public async Task DeleteAsync(Guid file)
        {
            using var client = CreateClient();
            await client.DeleteObjectAsync(_settings.BucketName, KeyOf(_settings, file));
        }
    }
}
