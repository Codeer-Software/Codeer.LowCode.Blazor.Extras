namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>監査ログの出力先。追記だけを持つ (更新・削除の口は無い。古い行の扱いは出力先の管理者の運用)。</summary>
    public interface IAuditSink
    {
        Task WriteAsync(AuditEvent auditEvent);
    }
}
