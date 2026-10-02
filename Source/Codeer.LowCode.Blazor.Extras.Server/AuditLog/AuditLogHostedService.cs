using Microsoft.Extensions.Hosting;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// アプリの起動・停止を System として記録する。
    /// 起動の記録は有効な設定 (FailureMode / 出力先) とデザインの版を残す = 設定の変化が追える。
    /// Strict で起動時に書けなければアプリは起動しない。
    /// </summary>
    public class AuditLogHostedService : IHostedService
    {
        readonly AuditLogger _auditLogger;
        readonly IEnumerable<IAuditSink> _sinks;

        public AuditLogHostedService(AuditLogger auditLogger, IEnumerable<IAuditSink> sinks)
        {
            _auditLogger = auditLogger;
            _sinks = sinks;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var s = _auditLogger.Settings;
            await _auditLogger.WriteAsync(new AuditEvent
            {
                Category = AuditCategory.System,
                Action = "Application.Start",
                Detail = $"FailureMode={s.FailureMode}; Sinks={string.Join(",", _sinks.Select(e => e.GetType().Name))}",
            });
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _auditLogger.WriteAsync(new AuditEvent { Category = AuditCategory.System, Action = "Application.Stop" });
            }
            catch (AuditLogException)
            {
                //停止中は失敗にする相手がいない (Critical には残っている)
            }
        }
    }
}
