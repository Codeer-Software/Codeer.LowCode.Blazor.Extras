using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// 今のリクエストの監査レコード (スコープ)。<see cref="AuditLogMiddleware"/> が誰が・どこから・何を・結果を埋め、
    /// コントローラはこれを注入して対象 (モジュール・Id) や失敗、ログイン時のユーザーを足す。書き込みはミドルウェアがレスポンスを返す前に行う。
    /// </summary>
    public class AuditContext
    {
        public AuditEvent Event { get; } = new();

        public void AddTarget(string module, string? id, string operation)
            => Event.Targets.Add(new AuditTarget { Module = module, Id = string.IsNullOrEmpty(id) ? null : id, Operation = operation });

        /// <summary>操作は HTTP としては成功したが業務としては失敗した (保存結果のエラーなど)。</summary>
        public void Fail(string reason)
        {
            Event.Result = AuditResult.Failure;
            Event.Detail = reason;
        }

        /// <summary>認証・認可で拒否した (ログイン失敗・二要素認証のコード不一致など)。</summary>
        public void Deny(string reason)
        {
            Event.Result = AuditResult.Denied;
            Event.Detail = reason;
        }

        /// <summary>
        /// 保存 (Submit) を包んで記録する。対象 (行ごとの Add / Update / Delete / SearchDelete) は送信内容から取り、
        /// 新規行の仮 Id は結果の採番 Id に置き換え、結果にエラーがあれば失敗にする。本体は保存の途中で新規行の Id を書き換えるので、対象は保存の前に取る。
        /// 保存が例外で終わっても対象は残す (結果はミドルウェアが例外から Failure にする)。
        /// </summary>
        public async Task<List<ModuleSubmitResult>> RecordSubmitAsync(IEnumerable<ModuleSubmitData> data, Func<Task<List<ModuleSubmitResult>>> submit)
        {
            var planned = data.Select(s => new
            {
                Adds = s.Add.Select(e => (e.Name, Id: ModuleDataValues.GetId(e))).ToList(),
                Updates = s.Update.Select(e => (e.Name, Id: ModuleDataValues.GetId(e))).ToList(),
                Deletes = s.Delete.Select(e => (e.ModuleName, e.Id)).ToList(),
                SearchDeletes = s.SearchDelete.Select(e => e.ModuleName).ToList(),
            }).ToList();

            List<ModuleSubmitResult>? results = null;
            try
            {
                results = await submit();
                return results;
            }
            finally
            {
                for (var i = 0; i < planned.Count; i++)
                {
                    var result = results != null && i < results.Count ? results[i] : null;
                    foreach (var (module, id) in planned[i].Adds) AddTarget(module, ResolveId(result, id), "Add");
                    foreach (var (module, id) in planned[i].Updates) AddTarget(module, id, "Update");
                    foreach (var (module, id) in planned[i].Deletes) AddTarget(module, id, "Delete");
                    foreach (var module in planned[i].SearchDeletes) AddTarget(module, null, "SearchDelete");
                    if (!string.IsNullOrEmpty(result?.ExceptionMessage)) Fail(result.ExceptionMessage);
                }
            }
        }

        /// <summary>一覧の読み出しの対象。返した行ごとに Read を記録する。</summary>
        public void AddRead(string module, Paging<ModuleData> page)
        {
            foreach (var e in page.Items) AddTarget(module, ModuleDataValues.GetId(e), "Read");
        }

        //新規行の仮 Id は採番後の Id に置き換える (結果の TemporaryIdMap / ルートは DestinationId)
        static string? ResolveId(ModuleSubmitResult? result, string? id)
        {
            if (result == null || string.IsNullOrEmpty(id)) return id;
            if (result.TemporaryIdMap.TryGetValue(id, out var assigned)) return assigned;
            return result.SourceId == id && !string.IsNullOrEmpty(result.DestinationId) ? result.DestinationId : id;
        }
    }
}
