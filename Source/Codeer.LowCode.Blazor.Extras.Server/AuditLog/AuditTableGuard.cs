using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// 監査ログのテーブルをアプリ経路 (モジュールの保存) から守る IO インターセプタ。
    /// 監査ログのテーブルに対して一覧モジュールを作って閲覧するのは通常のデザインでできるが、そのモジュールからの追加・更新・削除は拒否する
    /// (監査ログは追記専用。消せるのは保持期限の掃除と DB 管理者だけ)。
    /// ホストは <c>AddInterceptor(new AuditTableGuard(designData, settings))</c> で登録する。
    /// </summary>
    public class AuditTableGuard : IModuleDataIOInterceptor
    {
        readonly HashSet<string> _protectedModules;

        public AuditTableGuard(DesignData designData, AuditLogDatabaseSettings settings)
        {
            _protectedModules = designData.Modules.ToList()
                .Where(m => string.Equals(m.DataSourceName, settings.DataSourceName, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(m.DbTable, settings.Table, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name)
                .ToHashSet();
        }

        public Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIOInternalAccess io, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
        {
            if (_protectedModules.Count == 0) return next();
            var hit = transactionData.SelectMany(Modules).FirstOrDefault(_protectedModules.Contains);
            if (hit == null) return next();
            var message = $"The audit log is append-only. Saving through module '{hit}' is not allowed.";
            return Task.FromResult(transactionData.Select(e => new ModuleSubmitResult { SourceId = e.Id, DestinationId = e.Id, ExceptionMessage = message }).ToList());
        }

        public Task GetListAsync(ModuleDataIOInternalAccess io, SearchCondition condition, Paging<ModuleData> result) => Task.CompletedTask;

        static IEnumerable<string> Modules(ModuleSubmitData e)
            => new[] { e.ModuleName }
                .Concat(e.Add.Select(d => d.Name))
                .Concat(e.Update.Select(d => d.Name))
                .Concat(e.Delete.Select(d => d.ModuleName))
                .Concat(e.SearchDelete.Select(d => d.ModuleName));
    }
}
