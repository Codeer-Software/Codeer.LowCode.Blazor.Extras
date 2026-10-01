using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// 監査ログの IO インターセプタ。保存の合流点で 2 つのことをする。
    /// - 保存の対象を今のリクエストの監査レコード (<see cref="AuditContext.Current"/>) に記録する。
    ///   画面の保存・ファイル取込・スクリプトの一括保存はどれもここを通るので、経路によらず同じ形で残る (<see cref="AuditContext.RecordSubmitAsync"/>)
    /// - 監査ログのテーブルをモジュールの保存から守る。監査ログのテーブルに一覧モジュールを作って閲覧するのは通常のデザインでできるが、
    ///   そのモジュールからの追加・更新・削除は拒否する (監査ログは追記専用。消せるのは保持期限の掃除と DB 管理者だけ)
    /// ホストは <c>AddInterceptor(new AuditIOInterceptor(designData, settings))</c> で登録する。
    /// 保存の最終結果 (他のインターセプタが失敗にした結果も) を記録するので、他のインターセプタより先 (外側) に登録する。
    /// </summary>
    public class AuditIOInterceptor : IModuleDataIOInterceptor
    {
        readonly HashSet<string> _protectedModules;

        public AuditIOInterceptor(DesignData designData, AuditLogDatabaseSettings settings)
        {
            //DB に出力していなければ守るテーブルは無い
            _protectedModules = string.IsNullOrEmpty(settings.DataSourceName)
                ? []
                : designData.Modules.ToList()
                    .Where(m => string.Equals(m.DataSourceName, settings.DataSourceName, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(m.DbTable, settings.Table, StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Name)
                    .ToHashSet();
        }

        public Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIOInternalAccess io, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
        {
            var hit = _protectedModules.Count == 0 ? null : transactionData.SelectMany(Modules).FirstOrDefault(_protectedModules.Contains);
            if (hit != null)
            {
                var message = $"The audit log is append-only. Saving through module '{hit}' is not allowed.";
                next = () => Task.FromResult(transactionData.Select(e => new ModuleSubmitResult { SourceId = e.Id, DestinationId = e.Id, ExceptionMessage = message }).ToList());
            }
            //リクエストの外 (バックグラウンドのジョブ) や監査ログが無効のときは記録しない
            var audit = AuditContext.Current;
            return audit == null ? next() : audit.RecordSubmitAsync(transactionData, next);
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
