using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Extras.Server.Services
{
    /// <summary>
    /// SystemConfig の監査ログの出力先設定 (種類ごと: AuditLogDatabase / AuditLogFile) → IAuditSink の対応表
    /// (ファイル保存先の FileStorageTable と同じ考え方)。独自の出力先 (SIEM 直送など) を足すときは IAuditSink を実装してここに追加する。
    /// </summary>
    public static class AuditSinkTable
    {
        public static List<IAuditSink> Create()
        {
            var config = SystemConfig.Instance;
            var list = new List<IAuditSink>();
            //DB: 書き込みごとに専用の接続を開く (操作のトランザクションとは別。操作が失敗しても記録は残る)
            if (!string.IsNullOrEmpty(config.AuditLogDatabase.DataSourceName))
                list.Add(new DatabaseAuditSink(config.AuditLogDatabase, () => new DbAccessor(config.DataSources)));
            if (!string.IsNullOrEmpty(config.AuditLogFile.Directory))
                list.Add(new FileAuditSink(config.AuditLogFile));
            return list;
        }
    }
}
