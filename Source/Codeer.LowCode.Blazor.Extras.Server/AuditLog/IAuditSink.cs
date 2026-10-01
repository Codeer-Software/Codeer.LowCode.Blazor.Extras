namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>監査ログの出力先。追記と保持期限切れの削除だけを持つ (更新・個別削除の口は無い)。</summary>
    public interface IAuditSink
    {
        Task WriteAsync(AuditEvent auditEvent);

        /// <summary>これより古いレコードを消す。戻り値は消した数 (数えられない出力先は 0)。</summary>
        Task<int> PurgeAsync(DateTime olderThanUtc);
    }
}
