using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    /// <summary>書かれたレコードを溜めるだけの出力先。Fail を立てると書き込みで例外にする。</summary>
    sealed class CapturingAuditSink : IAuditSink
    {
        public List<AuditEvent> Events { get; } = new();
        public bool Fail { get; set; }

        public Task WriteAsync(AuditEvent auditEvent)
        {
            if (Fail) throw new IOException("sink down");
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }

    }
}
