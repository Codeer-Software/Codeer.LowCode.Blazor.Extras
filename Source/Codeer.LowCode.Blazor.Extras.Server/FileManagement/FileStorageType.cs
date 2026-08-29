namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>簡易形式 (<see cref="FileStorage"/>) で選べる保存先の種類。S3 や独自の保存先は種別別形式 (S3StorageSettings 等 + IFileStorage) で設定する。</summary>
    public enum FileStorageType
    {
        FileSystem,
        AzureBlobStorage,
    }
}
