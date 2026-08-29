using Codeer.LowCode.Blazor.SystemSettings;
using Extras.Client.Shared.Services;
using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Extras.Server.Services.DataChangeHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;

namespace Extras.Server.Services
{
    public class SystemConfig
    {
        public static SystemConfig Instance { get; set; } = new();

        public bool CanScriptDebug { get; set; }
        public bool UseHotReload { get; set; }
        public DataSource[] DataSources { get; set; } = [];
        //保存先は種類ごとの設定 (FileSystemStorages / AzureBlobStorages / S3Storages) を FileStorageTable が IFileStorage に組み立てる
        public List<IFileStorage> FileStorages { get; set; } = [];
        public DataChangeHistoryTableInfo[] DataChangeHistoryTableInfo { get; set; } = [];
        public TemporaryFileTableInfo[] TemporaryFileTableInfo { get; set; } = [];
        public string DesignFileDirectory { get; set; } = string.Empty;
        public string FontFileDirectory { get; set; } = string.Empty;
        //Mail = 製品(共通層)が読む設定。プロバイダごとの設定は個別のセクションとして持つ
        public MailConfig Mail { get; set; } = new();
        public SmtpSettings Smtp { get; set; } = new();
        public GmailSettings Gmail { get; set; } = new();
        public AISettings AISettings { get; set; } = new();
        public SystemConfigForFront ForFront() => new SystemConfigForFront { CanScriptDebug = CanScriptDebug, UseHotReload = UseHotReload };
    }
}
