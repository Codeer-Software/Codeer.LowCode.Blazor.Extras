namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    public class TemporaryFileTableInfo
    {
        public string DataSourceName { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string GuidColumn { get; set; } = string.Empty;
        public string CreatedDateTimeColumn { get; set; } = string.Empty;
        //任意。アップロードした人 (IAuthenticationContext.GetCurrentUserIdAsync) を記録し、一時ファイルを使える (保存で行に入れる・保存前に読む) のを同じ人に限る。空なら Guid だけで判定
        public string OwnerColumn { get; set; } = string.Empty;
    }
}
