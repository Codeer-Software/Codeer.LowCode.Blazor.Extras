using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>ホストの結線。<c>builder.Services.AddAuditLog(settings, sinks)</c> と、UseRouting の直後に <c>app.UseAuditLog()</c>。</summary>
    public static class AuditLogExtensions
    {
        /// <summary>
        /// <see cref="AuditLogger"/> (シングルトン)・<see cref="AuditContext"/> (スコープ)・起動/停止の <see cref="AuditLogHostedService"/> を登録する。
        /// 出力先はホストが設定から組み立てて渡す (DB なら <see cref="DatabaseAuditSink"/>、ファイルなら <see cref="FileAuditSink"/>、独自なら <see cref="IAuditSink"/>)。
        /// 無効 (Enabled=false) でも登録してよい (何も書かない)。
        /// </summary>
        /// <param name="designVersion">
        /// デザインの版 (App.zip の SHA-256)。HttpContext があればそのリクエストが使う版 (リクエストの間は変わらない)、
        /// null ならこのプロセスが今読み込んでいる版を返す。渡せば全レコードに版が入り、版の切替も記録される。
        /// </param>
        public static IServiceCollection AddAuditLog(this IServiceCollection services, AuditLogSettings settings, IEnumerable<IAuditSink> sinks, Func<HttpContext?, string>? designVersion = null)
        {
            var sinkList = sinks.ToList();
            foreach (var sink in sinkList) services.AddSingleton(sink);
            services.AddSingleton(sp => new AuditLogger(settings, sinkList, sp.GetService<ILoggerFactory>()?.CreateLogger<AuditLogger>(), designVersion));
            services.AddScoped<AuditContext>();
            services.AddHostedService<AuditLogHostedService>();
            return services;
        }

        /// <summary>WebAPI の記録 (<see cref="AuditLogMiddleware"/>)。UseRouting の直後 (認可の前) に置く。認証はミドルウェアが自分で解決する。</summary>
        public static IApplicationBuilder UseAuditLog(this IApplicationBuilder app)
            => app.UseMiddleware<AuditLogMiddleware>();
    }
}
