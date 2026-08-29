# FileStorage - ファイルの保存先

FileField がアップロードしたファイルの実体を置く場所です。サーバー側 (`Codeer.LowCode.Blazor.Extras.Server`) の
`FileManagement` が担当します。FileField 側はデザインで `StorageName` を指定して保存先を選びます。

設定の持ち方は 2 つあります。テンプレートが使うのは種別別形式で、メール送信と同じく**保存先の種類ごとに独立した設定クラスと appsettings セクション**を持ち、
アプリ (テンプレートの `FileStorageTable`) がそれぞれを読んで `IFileStorage` の並びにして製品へ渡します。
製品側は種類ごとの差を `IFileStorage` で吸収するので、独自の保存先も同じ並びに足すだけです。

## 組み込みの保存先

| 設定クラス / 実装 | 保存先 | セクション (テンプレート既定) | プロパティ |
|---|---|---|---|
| `FileSystemStorageSettings` / `FileSystemFileStorage` | サーバーのフォルダ | `FileSystemStorages` | `Name` / `Directory` |
| `AzureBlobStorageSettings` / `AzureBlobFileStorage` | Azure Blob Storage | `AzureBlobStorages` | `Name` / `ContainerName` / `ConnectionString` または `BlobServiceUri` |
| `S3StorageSettings` / `S3FileStorage` | Amazon S3、および S3 互換 API (MinIO / Cloudflare R2 / Wasabi など) | `S3Storages` | `Name` / `BucketName` / `Region` / `ServiceUrl` / `ProfileName` / `KeyPrefix` |

ファイルはどの保存先でも Guid をキーにフラットに置かれます (フォルダ階層は作りません)。

## appsettings 例

```json
"FileSystemStorages": [
  { "Name": "Local", "Directory": "C:\\Files" }
],
"AzureBlobStorages": [
  { "Name": "Azure", "BlobServiceUri": "https://myaccount.blob.core.windows.net", "ContainerName": "files" },
  { "Name": "AzureByKey", "ContainerName": "files" }
],
"S3Storages": [
  { "Name": "S3", "BucketName": "my-app-files", "Region": "ap-northeast-1" },
  {
    "Name": "MinIO", "BucketName": "files",
    "ServiceUrl": "http://localhost:9000", "Region": "us-east-1",
    "ProfileName": "minio-local"
  }
]
```

### S3 のプロパティ

| プロパティ | 説明 |
|---|---|
| `BucketName` | バケット名 (必須) |
| `Region` | リージョン (例 `ap-northeast-1`)。`ServiceUrl` 指定時は署名用リージョンとして使われ、省略可 |
| `ServiceUrl` | S3 互換ストレージのエンドポイント。指定するとパス形式アドレッシング (`host/bucket/key`) になる。AWS 本体では指定しない |
| `ProfileName` | 使う共有プロファイル名 (`~/.aws/credentials`)。空なら SDK 既定の解決 |

### Azure の資格情報

どちらでも接続できます。

- **`ConnectionString` を指定** → 接続文字列 (共有キー) で接続。`ConnectionStrings:<Name>` に置いてもよい (上の例の `AzureByKey`)。DB と同じ運用でよい場合
- **`ConnectionString` が空** → `BlobServiceUri` に対して `DefaultAzureCredential` で認証
  (環境変数 → Workload Identity → **Managed Identity** (App Service / Container Apps / VM) → Visual Studio / Azure CLI のログイン)。
  ストレージアカウント (またはコンテナ) に「ストレージ BLOB データ共同作成者」ロールを付ける。キーを一切持たないので本番はこちらを推奨

### S3 の資格情報

アクセスキーを appsettings に書く設定は**用意していません**。AWS SDK の標準の資格情報解決に任せます。

- **本番**: EC2 / ECS / EKS(IRSA) の IAM ロール。設定は `BucketName` と `Region` だけ
- **ローカル開発**: 環境変数 `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` か、`~/.aws/credentials` の名前付きプロファイルを `ProfileName` で指定 (MinIO 等の S3 互換も同じ。プロファイルにそのストレージ用のキーを置く)
- 複数バケットで別アカウントを使うときも `ProfileName` で切り替える
| `KeyPrefix` | オブジェクトキーの接頭辞 (例 `attachments/`)。1 バケットを複数用途で共有するとき用 |

## アプリ側の結線 (テンプレート)

```csharp
// Services/FileStorageTable.cs
public static List<IFileStorage> Create(IConfiguration config)
{
    var list = new List<IFileStorage>();
    foreach (var e in config.GetSection("FileSystemStorages").Get<FileSystemStorageSettings[]>() ?? [])
        list.Add(new FileSystemFileStorage(e));
    foreach (var e in config.GetSection("AzureBlobStorages").Get<AzureBlobStorageSettings[]>() ?? [])
    {
        //接続文字列は ConnectionStrings:<Name> にも置ける (無ければ BlobServiceUri + DefaultAzureCredential)
        if (string.IsNullOrEmpty(e.ConnectionString) && string.IsNullOrEmpty(e.BlobServiceUri)) e.ConnectionString = config.GetConnectionString(e.Name) ?? string.Empty;
        list.Add(new AzureBlobFileStorage(e));
    }
    foreach (var e in config.GetSection("S3Storages").Get<S3StorageSettings[]>() ?? [])
        list.Add(new S3FileStorage(e));
    return list;
}
```

`TemporaryFileManager` と `StorageAccess` にはこの並びを渡します。独自の保存先は `IFileStorage` を実装して同じ並びに足してください。
テンプレートの `FileStorageTable` は下の簡易形式 (`FileStorages`) も読んで同じ並びに加えるので、両形式を混在させられます。

## 簡易形式 (`FileStorages` + `FileStorageType`)

FileSystem / Azure Blob (接続文字列) だけなら、種別と設定を 1 クラス (`FileStorage`) に持つ簡易形式でも設定できます (従来からの形式)。
Azure の接続文字列は `ConnectionStrings:<Name>` に置くこともできます。

```json
"FileStorages": [
  { "Name": "Local", "FileStorageType": "FileSystem", "Directory": "C:\Files" },
  { "Name": "Azure", "FileStorageType": "AzureBlobStorage", "ContainerName": "files" }
]
```

`StorageAccess` / `TemporaryFileManager` には `FileStorage[]` をそのまま渡せます (内部で `FileStorage.ToFileStorage()` により同じ `IFileStorage` に変換される)。
S3 や独自の保存先を使うときは上の種別別形式にしてください。
