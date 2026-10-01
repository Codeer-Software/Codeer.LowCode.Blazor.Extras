using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// アプリの起動・停止を System として記録し、保持期限切れの削除を 1 日 1 回行う。
    /// 起動の記録は有効な設定 (FailureMode / RetentionDays / Categories / 出力先) を Detail に残す = 設定の変化が追える。
    /// Strict で起動時に書けなければアプリは起動しない。
    /// </summary>
    public class AuditLogHostedService : BackgroundService
    {
        readonly AuditLogger _auditLogger;
        readonly IEnumerable<IAuditSink> _sinks;
        readonly ILogger<AuditLogHostedService>? _logger;

        public AuditLogHostedService(AuditLogger auditLogger, IEnumerable<IAuditSink> sinks, ILogger<AuditLogHostedService>? logger = null)
        {
            _auditLogger = auditLogger;
            _sinks = sinks;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            var s = _auditLogger.Settings;
            await _auditLogger.WriteAsync(new AuditEvent
            {
                Category = AuditCategory.System,
                Action = "Application.Start",
                Detail = $"FailureMode={s.FailureMode}; RetentionDays={s.RetentionDays}; Categories={(s.Categories.Length == 0 ? "All" : string.Join(",", s.Categories))}; AttemptCategories={string.Join(",", s.EffectiveAttemptCategories)}; Sinks={string.Join(",", _sinks.Select(e => e.GetType().Name))}",
            });
            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _auditLogger.PurgeAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Audit log purge failed.");
                }
                try { await Task.Delay(TimeSpan.FromDays(1), stoppingToken); } catch (OperationCanceledException) { }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
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
