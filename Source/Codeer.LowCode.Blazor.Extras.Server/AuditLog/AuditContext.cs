using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// 今のリクエストの監査レコード (スコープ)。<see cref="AuditLogMiddleware"/> が誰が・どこから・何を・結果を埋め、
    /// 処理の側 (コントローラ・保存のインターセプタ・一括ファイル・メール) が対象 (モジュール・Id)・件数・失敗を足す。
    /// 書き込みはミドルウェアがレスポンスを返す前に行う。
    /// コントローラは DI で受け取り、公開メソッドの引数に持ち回れない処理は <see cref="Current"/> から取る。
    /// </summary>
    public class AuditContext
    {
        static readonly AsyncLocal<AuditContext?> _current = new();

        /// <summary>今のリクエストの監査レコード。リクエストの外 (バックグラウンドのジョブ) や監査ログが無効のときは null。</summary>
        public static AuditContext? Current
        {
            get => _current.Value;
            internal set => _current.Value = value;
        }

        readonly List<string> _countNames = new();
        readonly Dictionary<string, int> _counts = new();
        readonly List<string> _notes = new();

        public AuditEvent Event { get; } = new();

        public void AddTarget(string module, string? id, string operation)
            => Event.Targets.Add(new AuditTarget { Module = module, Id = string.IsNullOrEmpty(id) ? null : id, Operation = operation });

        /// <summary>件数 (追加・更新・削除した行数、出力した行数、送信数など)。同じ名前は足し合わせ、Detail の先頭に <c>名前=件数</c> で入る。</summary>
        public void AddCount(string name, int count)
        {
            if (!_counts.ContainsKey(name)) _countNames.Add(name);
            _counts[name] = _counts.GetValueOrDefault(name) + count;
        }

        /// <summary>補足 (取り込んだファイルのハッシュなど)。Detail に <c>名前=値</c> で入る。レコードの値は入れない。</summary>
        public void AddNote(string name, string value)
            => _notes.Add($"{name}={value}");

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

        /// <summary>一覧の読み出しの対象。返した行ごとに Read を記録する。</summary>
        public void AddRead(string module, Paging<ModuleData> page)
        {
            foreach (var e in page.Items) AddTarget(module, ModuleDataValues.GetId(e), "Read");
        }

        //Detail の最終形: 件数 → 補足 → 理由など (Event.Detail)
        internal string ComposeDetail()
        {
            var parts = _countNames.Select(e => $"{e}={_counts[e]}").Concat(_notes).ToList();
            if (!string.IsNullOrEmpty(Event.Detail)) parts.Add(Event.Detail);
            return string.Join("; ", parts);
        }

        /// <summary>
        /// 保存 (Submit) を包んで記録する。保存の合流点 (<see cref="AuditIOInterceptor"/>) から呼ばれるので、
        /// 画面の保存・ファイル取込・スクリプトの一括保存のどれも同じ形で残る。
        /// - 対象は行ごとの Add / Update / Delete と Id (条件での削除 SearchDelete はモジュール名だけ)。件数は AddCount で Detail に入る
        /// - 本体は保存の途中で新規行の Id を書き換えるので、対象は保存の前に取り、新規行の仮 Id は結果の採番 Id に置き換える。
        ///   Id の無い新規行 (ファイル取込の自動採番) には、採番 Id を結果から引くために仮 Id を付ける。
        ///   記録が済んだら、呼び出し元へ返す結果は仮 Id を付ける前の形に戻す (監査ログの有無で保存の応答を変えない)
        /// - 本体の一括 INSERT (BulkAddThreshold 以上の純粋な追加) は採番 Id を返さない。その新規行は Id 無しで、モジュールごとに 1 件の Add と件数だけが残る
        /// - 結果にエラーがあれば失敗にする。保存が例外で終わっても対象は残す (権限拒否 LowCodeAccessDeniedException は Denied、他の例外はミドルウェアが Failure にする)
        /// </summary>
        internal async Task<List<ModuleSubmitResult>> RecordSubmitAsync(List<ModuleSubmitData> data, Func<Task<List<ModuleSubmitResult>>> submit)
        {
            var assignedIds = data.Select(AssignTemporaryIdToRootAdd).ToList();
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
            catch (LowCodeAccessDeniedException ex)
            {
                //本体の権限拒否は型のまま上がってくる (ModuleDataIO.SubmitAsync が呼び出し元には ExceptionMessage にする)。対象は finally が残す
                Deny(ex.Message);
                throw;
            }
            finally
            {
                var addsWithoutId = new HashSet<string>();
                for (var i = 0; i < planned.Count; i++)
                {
                    var result = results != null && i < results.Count ? results[i] : null;
                    foreach (var (module, id) in planned[i].Adds)
                    {
                        var assigned = ResolveId(result, id);
                        //採番 Id が分からない新規行 (一括 INSERT・保存の失敗) は、モジュールごとに 1 件にまとめる (件数は AddCount にある)
                        if (!string.IsNullOrEmpty(assigned)) AddTarget(module, assigned, "Add");
                        else if (addsWithoutId.Add(module)) AddTarget(module, null, "Add");
                    }
                    foreach (var (module, id) in planned[i].Updates) AddTarget(module, id, "Update");
                    foreach (var (module, id) in planned[i].Deletes) AddTarget(module, id, "Delete");
                    foreach (var module in planned[i].SearchDeletes) AddTarget(module, null, "SearchDelete");
                    if (!string.IsNullOrEmpty(result?.ExceptionMessage)) Fail(result.ExceptionMessage);
                }
                AddCount("Add", planned.Sum(e => e.Adds.Count));
                AddCount("Update", planned.Sum(e => e.Updates.Count));
                AddCount("Delete", planned.Sum(e => e.Deletes.Count));
                var searchDeletes = planned.Sum(e => e.SearchDeletes.Count);
                if (searchDeletes != 0) AddCount("SearchDelete", searchDeletes);
                RestoreAssignedIds(data, results, assignedIds);
            }
        }

        //Id の無いルートの新規行に仮 Id を付ける (1 行ずつの保存なら、結果の TemporaryIdMap / DestinationId から採番 Id を引ける)。
        //付けたら (仮 Id, 元の Id) を返す。付けなければ null
        static (string TempId, string? OriginalId)? AssignTemporaryIdToRootAdd(ModuleSubmitData submitData)
        {
            if (!string.IsNullOrEmpty(submitData.Id)) return null;
            var root = submitData.Add.FirstOrDefault(e => e.Name == submitData.ModuleName && string.IsNullOrEmpty(ModuleDataValues.GetId(e)));
            if (root == null) return null;
            var originalId = submitData.Id;
            var tempId = IdFieldData.NewId();
            root.Fields[SystemFieldNames.Id] = tempId;
            submitData.Id = tempId.Value!;
            return (tempId.Value!, originalId);
        }

        //監査のために付けた仮 Id の痕跡を、呼び出し元へ返す結果から消す (SourceId / DestinationId を元に戻し、対応表から外す)。
        //対応表は全行の結果が同じものを持つので、残すと行数の多い取込で応答が行数の 2 乗に膨らむ
        static void RestoreAssignedIds(List<ModuleSubmitData> data, List<ModuleSubmitResult>? results, List<(string TempId, string? OriginalId)?> assignedIds)
        {
            for (var i = 0; i < data.Count; i++)
            {
                if (assignedIds[i] is not { } assigned) continue;
                data[i].Id = assigned.OriginalId!;
                var result = results != null && i < results.Count ? results[i] : null;
                if (result == null) continue;
                if (result.SourceId == assigned.TempId)
                {
                    result.SourceId = assigned.OriginalId!;
                    result.DestinationId = assigned.OriginalId!;
                }
            }
            if (results == null) return;
            var tempIds = assignedIds.Where(e => e != null).Select(e => e!.Value.TempId).ToList();
            foreach (var map in results.Select(e => e.TemporaryIdMap).Distinct(ReferenceEqualityComparer.Instance).Cast<Dictionary<string, string>>())
                foreach (var tempId in tempIds) map.Remove(tempId);
        }

        //新規行の仮 Id は採番後の Id に置き換える (結果の TemporaryIdMap / ルートは DestinationId)。解決できなければ Id 無し (仮 Id は残さない)
        static string? ResolveId(ModuleSubmitResult? result, string? id)
        {
            if (!IdFieldData.IsTemporaryId(id)) return id;
            if (result == null) return null;
            if (result.TemporaryIdMap.TryGetValue(id!, out var assigned)) return assigned;
            return result.SourceId == id && !IdFieldData.IsTemporaryId(result.DestinationId) ? result.DestinationId : null;
        }
    }
}
