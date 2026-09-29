using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.EditHistory
{
    /// <summary>
    /// 編集履歴。ModuleDataIO のインターセプタとして 1 つ登録すると、保存と読み出しの両方に効く
    /// (<c>AddInterceptor(new EditHistoryRecorder(designData))</c>)。
    /// - 保存: EditHistoryField を置いたモジュールのルートレコード 1 件につき履歴モジュールに 1 行 (レコード全体のスナップショット) を書く。
    ///   削除は本体の前に削除前の内容を、作成・更新は本体の後に保存後の内容を、内部読み (操作ユーザーの権限に関係なく全列・全従属レコード) で読む。
    ///   履歴の書き込みは内部 add 経路 (操作ユーザーの書き込み権限に依存しない)。履歴モジュールの版はシステムだけが書く
    ///   (画面・API からの追加・更新は拒否する。古い版の削除はできる)。
    /// - 読み出し: 履歴モジュールの行の Snapshot を読む人の権限に落として返す (対象モジュールの読めない列・読めない子モジュールの従属レコード・
    ///   行の閲覧条件に合わない行。対象モジュール自体を読めない・行が条件に合わないなら空)。一覧・詳細・ダウンロードは全部ここを通る。
    /// - 復活 / この版に戻す: 送信に同梱された削除の取り消しの依頼を、本体の保存の前に処理する (EditHistoryUndeleter)。
    ///   戻せないものが 1 つでもあれば保存全体を失敗にする (部分反映はしない)。
    /// </summary>
    /// <remarks>
    /// 履歴の記録に失敗したときは結果に ExceptionMessage を立てて保存ごと失敗 (ロールバック) にする
    /// (履歴が静かに欠けるより、保存できないことがユーザーに見える方がよい)。
    /// EditHistoryField があるのに履歴モジュール・契約が無い設計 (デザインチェックが指摘する不備) も同様に失敗にする。
    /// 一括取込 (ファイル / スクリプトの一括保存) も記録する。Id 空の新規行には仮 Id を付けて採番 Id を引く。
    /// 履歴対象モジュールの投入は本体の一括 INSERT 経路 (BulkAddThreshold 以上の純追加・採番 Id が返らない) を使わず、
    /// 1 行ずつの経路に落とす (NoTemporaryIdResolution を外す)。
    /// </remarks>
    public class EditHistoryRecorder : IModuleDataIOInterceptor
    {
        readonly DesignData _designData;
        readonly Action<string>? _logError;
        readonly EditHistoryUndeleter _undeleter;

        /// <param name="logError">記録をスキップした理由・復活の失敗理由などを出すログ (ILogger の Warning 等)。</param>
        public EditHistoryRecorder(DesignData designData, Action<string>? logError = null)
        {
            _designData = designData;
            _logError = logError;
            _undeleter = new EditHistoryUndeleter(designData);
        }

        // ===== 読み出し: Snapshot を読む人の権限に落とす =====

        public async Task GetListAsync(ModuleDataIOInternalAccess io, SearchCondition condition, Paging<ModuleData> result)
        {
            var names = EditHistoryContracts.Contract(_designData.Modules.Find(condition.ModuleName));
            if (names == null || string.IsNullOrEmpty(names.Snapshot)) return;
            foreach (var row in result.Items)
            {
                if (row.Fields.GetValueOrDefault(names.Snapshot) is not TextFieldData snapshotData || string.IsNullOrEmpty(snapshotData.Value)) continue;
                snapshotData.Value = await ToReadableAsync(io, snapshotData.Value);
            }
        }

        //スナップショット JSON を読む人の権限に落とす。壊れている・落とす途中で失敗・対象モジュールを読めない・行が条件に合わない、なら空 (版は出るが内容は見えない)
        async Task<string> ToReadableAsync(ModuleDataIOInternalAccess io, string json)
        {
            try
            {
                var snapshot = EditHistorySnapshot.Deserialize(json);
                if (snapshot == null || !await io.RemoveUnreadableAsync(snapshot)) return string.Empty;
                return EditHistorySnapshot.Serialize(snapshot);
            }
            catch (Exception ex)
            {
                _logError?.Invoke($"Edit history snapshot could not be read: {ex.Message}");
                return string.Empty;
            }
        }

        // ===== 保存: 復活 → 本体の保存 → 記録 =====

        public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIOInternalAccess io, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
        {
            //履歴モジュールの版はシステムだけが書く (画面・API からの追加・更新は拒否。削除は古い版の整理のために許す)
            foreach (var submitData in transactionData)
            {
                var historyModule = _designData.Modules.Find(submitData.ModuleName);
                if (historyModule == null || EditHistoryContracts.Contract(historyModule) == null) continue;
                if (submitData.Add.Count != 0 || submitData.Update.Count != 0)
                    return Fail(transactionData, $"The edit history module '{historyModule.Name}' is written only by the system. Versions cannot be added or changed.");
            }

            //削除の取り消し (この版に戻す / 復活ボタン)。本体の前に戻しておけば、同じ Submit の Update と一緒に確定する
            var restores = new Dictionary<int, EditHistoryUndeleteResult>();
            for (var i = 0; i < transactionData.Count; i++)
            {
                var submitData = transactionData[i];
                foreach (var request in submitData.ExtendedData.OfType<EditHistoryUndeleteData>())
                {
                    try
                    {
                        var restored = await _undeleter.RestoreAsync(io, submitData, request);
                        if (restored.IsWholeRecord) restores[i] = restored;
                    }
                    catch (Exception ex)
                    {
                        return Fail(transactionData, ex.Message);
                    }
                }
            }

            var plans = new List<Plan>();
            for (var i = 0; i < transactionData.Count; i++)
            {
                var submitData = transactionData[i];
                var module = _designData.Modules.Find(submitData.ModuleName);
                if (module == null) continue;
                var resolved = EditHistoryContracts.Resolve(_designData, module, out var error);
                if (error != null) return Fail(transactionData, error);
                var isRootDelete = submitData.Delete.Any(e => e.ModuleName == submitData.ModuleName && e.Id == submitData.Id);
                //履歴を持つモジュールのレコードに触る送信では、削除・差し替えで消える添付ファイルを残す (版から戻したときに実体がある)
                if (resolved != null || TouchesHistoryModule(submitData)) io.KeepDeletedFiles(submitData);
                if (resolved == null)
                {
                    //履歴を持たないモジュールの削除でも、一緒に消える従属レコードのうち自分の履歴を持つモジュールの行には削除の版を残す
                    if (isRootDelete && IndividualTargets(module, null, out var targetError).Count != 0)
                    {
                        if (targetError != null) return Fail(transactionData, targetError);
                        var full = await LoadFullAsync(io, module.Name, submitData.Id);
                        if (full != null)
                        {
                            var (rowPlans, rowError) = await IndividuallyRecordedDeletePlansAsync(io, i, module, full);
                            if (rowError != null) return Fail(transactionData, rowError);
                            plans.AddRange(rowPlans);
                        }
                    }
                    continue;
                }
                //履歴対象の投入は結果の仮 Id 解決を使う (= 本体の一括 INSERT 経路の対象外にし、1 行ずつの経路で採番 Id を得る)
                submitData.NoTemporaryIdResolution = false;
                AssignTemporaryIdToRootAdd(submitData);

                var plan = new Plan
                {
                    Index = i, Module = module, HistoryModule = resolved.Value.HistoryModule, Names = resolved.Value.Names,
                };
                if (restores.TryGetValue(i, out var restored))
                {
                    //復活ボタン: 論理削除は同じ Id、物理削除は作り直した Id (旧 Id の版は作り直した Id に付け替え済み = 履歴は繋がる)
                    plan.ChangeType = EditHistoryChangeType.Restore;
                    plan.FixedId = restored.RestoredId;
                }
                else if (isRootDelete)
                {
                    plan.ChangeType = EditHistoryChangeType.Delete;
                    //削除の版は削除前の内容。行ごとに記録する従属レコードの行も親の削除で消えるので、その削除の版も残す (削除前に読んでおく)
                    var full = await LoadFullAsync(io, module.Name, submitData.Id);
                    plan.Snapshot = EditHistoryPolicy.Strip(_designData, EditHistoryContracts.Field(module), full?.JsonClone());
                    if (full != null)
                    {
                        var (rowPlans, rowError) = await IndividuallyRecordedDeletePlansAsync(io, i, module, full);
                        if (rowError != null) return Fail(transactionData, rowError);
                        plans.AddRange(rowPlans);
                    }
                }
                else if (IsStandalone(module, submitData))
                {
                    //ExecuteSqlField (Standalone) だけの送信: レコード自体が変わったときだけ版にする (変更前を取っておいて後で比べる)
                    if (string.IsNullOrEmpty(submitData.Id) || IdFieldData.IsTemporaryId(submitData.Id)) continue;
                    plan.ChangeType = EditHistoryChangeType.Update;
                    plan.Before = await LoadAsync(io, module.Name, submitData.Id);
                }
                else if (IsEmpty(submitData))
                {
                    //何も保存しない送信 (承認の申請で申請書に変更が無いときなど。本体は何も書かない) は版にしない
                    continue;
                }
                else if (OnlyNotIncludedRows(module, submitData))
                {
                    //親の版に含めない従属レコード (除外・行ごと) の行だけの送信: 親には変更が無いので親の版にしない (行ごとの版は下で作る)
                    continue;
                }
                else
                {
                    plan.ChangeType = IsRootAdd(submitData) ? EditHistoryChangeType.Add : EditHistoryChangeType.Update;
                }
                plans.Add(plan);
            }

            //行ごとに記録する従属レコード (親の EditHistoryField の IndividuallyRecordedOwnedRecords。親に EditHistoryField が無ければ、
            //自分の EditHistoryField を持つ従属レコードのモジュール全部):
            //親の送信に乗った行 (Add / Update / Delete) を、行のモジュール自身の履歴に 1 行 1 版で記録する。
            //親の画面から保存しても、行のモジュールで保存したのと同じ版になる。削除は本体の前に削除前の内容を読む
            for (var i = 0; i < transactionData.Count; i++)
            {
                var submitData = transactionData[i];
                var module = _designData.Modules.Find(submitData.ModuleName);
                var field = EditHistoryContracts.Field(module);
                if (module == null || (field != null && field.IndividuallyRecordedOwnedRecords.Count == 0)) continue;

                var targets = IndividualTargets(module, field, out var targetError);
                if (targetError != null) return Fail(transactionData, targetError);
                if (targets.Count == 0) continue;

                foreach (var e in submitData.Add)
                {
                    if (targets.TryGetValue(e.Name, out var t)) plans.Add(RowPlan(i, t, EditHistoryChangeType.Add) with { RowTempId = ModuleDataValues.GetId(e) });
                }
                foreach (var e in submitData.Update)
                {
                    if (targets.TryGetValue(e.Name, out var t)) plans.Add(RowPlan(i, t, EditHistoryChangeType.Update) with { RowId = ModuleDataValues.GetId(e) });
                }
                foreach (var d in submitData.Delete)
                {
                    if (!targets.TryGetValue(d.ModuleName, out var t)) continue;
                    plans.Add(RowPlan(i, t, EditHistoryChangeType.Delete) with { RowId = d.Id, Snapshot = await LoadAsync(io, d.ModuleName, d.Id) });
                }
            }

            var results = await next();
            if (results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage))) return results;
            //物理削除からの復活は作り直した Id をクライアントに返す (復活後の遷移先)
            foreach (var (index, restored) in restores)
            {
                if (restored.IsRecreated && index < results.Count) results[index].DestinationId = restored.RestoredId;
            }
            if (plans.Count == 0) return results;

            try
            {
                var user = await io.GetCurrentUserAsync();
                var userId = user == null ? string.Empty : ModuleDataValues.GetId(user);
                var localNow = DateTime.Now;
                var utcNow = DateTime.UtcNow;
                foreach (var plan in plans)
                {
                    string id;
                    if (plan.RowTempId != null)
                    {
                        //行ごとの作成: 送信の仮 Id を結果の対応表で採番 Id にしてから保存後の行を読む
                        var map = plan.Index < results.Count ? results[plan.Index].TemporaryIdMap : null;
                        id = map != null && map.TryGetValue(plan.RowTempId, out var real) ? real : plan.RowTempId;
                        if (string.IsNullOrEmpty(id) || IdFieldData.IsTemporaryId(id))
                        {
                            _logError?.Invoke($"Edit history of a row of '{plan.Module.Name}' was not recorded: the Id of the saved row is not available.");
                            continue;
                        }
                        plan.Snapshot = await LoadAsync(io, plan.Module.Name, id);
                    }
                    else if (plan.RowId != null)
                    {
                        id = plan.RowId;
                        if (plan.ChangeType != EditHistoryChangeType.Delete) plan.Snapshot = await LoadAsync(io, plan.Module.Name, id);
                    }
                    else if (plan.FixedId != null)
                    {
                        id = plan.FixedId;
                        plan.Snapshot = await LoadAsync(io, plan.Module.Name, id);
                    }
                    else if (plan.ChangeType == EditHistoryChangeType.Delete)
                    {
                        id = transactionData[plan.Index].Id;
                    }
                    else
                    {
                        id = plan.Index < results.Count ? results[plan.Index].DestinationId : string.Empty;
                        if (string.IsNullOrEmpty(id) || IdFieldData.IsTemporaryId(id))
                        {
                            _logError?.Invoke($"Edit history of '{plan.Module.Name}' was not recorded: the Id of the saved record is not available (bulk insert, or an ExecuteSqlField create without NewId).");
                            continue;
                        }
                        plan.Snapshot = await LoadAsync(io, plan.Module.Name, id);
                        //Standalone の SQL でレコードが変わっていなければ版にしない
                        if (plan.Before != null && plan.Snapshot != null &&
                            EditHistorySnapshot.Serialize(plan.Before) == EditHistorySnapshot.Serialize(plan.Snapshot)) continue;
                    }
                    if (plan.Snapshot == null)
                    {
                        _logError?.Invoke($"Edit history of '{plan.Module.Name}' ({id}) was not recorded: the record could not be read.");
                        continue;
                    }
                    await WriteAsync(io, plan, id, userId, localNow, utcNow);
                }
            }
            catch (Exception ex)
            {
                var message = $"Failed to record the edit history: {ex.Message}";
                _logError?.Invoke(message);
                foreach (var result in results) result.ExceptionMessage = message;
            }
            return results;
        }

        // ===== 記録の元を読む =====

        //レコード + 従属レコード (明細・埋め込みモジュールの子レコード等、宣言の先) を内部読み (権限に関係なく全列・全従属) で読み、
        //NULL だった列も null として持たせる (版に「空だった」を残す = 復元で空に戻せる)。
        //含めない従属レコード (そのモジュールの EditHistoryField の除外・行ごと指定) は読まない
        async Task<ModuleData?> LoadAsync(ModuleDataIOInternalAccess io, string moduleName, string id)
        {
            var field = EditHistoryContracts.Field(_designData.Modules.Find(moduleName));
            return EditHistorySnapshot.FillNulls(_designData, await io.GetWithOwnedRecordsAsync(moduleName, id, path => EditHistoryPolicy.IsIncluded(field, path)));
        }

        //行ごとに記録する宣言の先も読む (除外は読まない)。親の削除で消える「行ごとに記録する行」を見つけるため
        async Task<ModuleData?> LoadFullAsync(ModuleDataIOInternalAccess io, string moduleName, string id)
        {
            var field = EditHistoryContracts.Field(_designData.Modules.Find(moduleName));
            return EditHistorySnapshot.FillNulls(_designData, await io.GetWithOwnedRecordsAsync(moduleName, id, path => !EditHistoryPolicy.IsExcluded(field, path)));
        }

        //行ごとに記録する宣言の先のモジュール → 記録先 (履歴モジュール・契約)。
        //親に EditHistoryField があれば IndividuallyRecordedOwnedRecords の宣言の先。
        //親に EditHistoryField が無ければ (親に版が無い)、従属レコードのうち自分の EditHistoryField を持つモジュール全部 = 子は自分の履歴に行ごとに残す
        Dictionary<string, (ModuleDesign HistoryModule, EditHistoryContractFieldDesign Names, ModuleDesign Row)> IndividualTargets(ModuleDesign module, EditHistoryFieldDesign? field, out string? error)
        {
            error = null;
            var targets = new Dictionary<string, (ModuleDesign, EditHistoryContractFieldDesign, ModuleDesign)>();
            foreach (var (path, _, _, _, child) in EditHistoryPolicy.Walk(_designData, module, field, descendIntoNotIncluded: true))
            {
                if (child == null || child.Name == module.Name || targets.ContainsKey(child.Name)) continue;
                if (field != null ? !EditHistoryPolicy.IsIndividual(field, path) : EditHistoryContracts.Field(child) == null) continue;
                var resolved = EditHistoryContracts.Resolve(_designData, child, out var rowError);
                if (rowError != null)
                {
                    error = rowError;
                    return targets;
                }
                if (resolved == null)
                {
                    _logError?.Invoke($"Edit history of the rows of '{child.Name}' (owned records '{path}' of '{module.Name}') was not recorded: '{child.Name}' has no EditHistoryField.");
                    continue;
                }
                targets[child.Name] = (resolved.Value.HistoryModule, resolved.Value.Names, child);
            }
            return targets;
        }

        //親の削除 (従属レコードも一緒に消える) で、行ごとに記録する宣言の先の行の削除の版を作る。
        //行は削除前に、行のモジュール自身の履歴の規則 (そのモジュールの EditHistoryField の除外・行ごと) で読む (行のモジュールの画面で保存した版と同じ内容になる)
        async Task<(List<Plan> Plans, string? Error)> IndividuallyRecordedDeletePlansAsync(ModuleDataIOInternalAccess io, int index, ModuleDesign module, ModuleData full)
        {
            var field = EditHistoryContracts.Field(module);
            if (field != null && field.IndividuallyRecordedOwnedRecords.Count == 0) return (new(), null);
            var targets = IndividualTargets(module, field, out var error);
            if (error != null) return (new(), error);
            var plans = new List<Plan>();
            foreach (var (path, rowDesign, row) in OwnedRows(module, full, string.Empty))
            {
                if ((field != null && !EditHistoryPolicy.IsIndividual(field, path)) || !targets.TryGetValue(rowDesign.Name, out var t)) continue;
                var id = EditHistorySnapshot.GetId(row);
                if (id.Length == 0) continue;
                plans.Add(RowPlan(index, t, EditHistoryChangeType.Delete) with { RowId = id, Snapshot = await LoadAsync(io, rowDesign.Name, id) });
            }
            return (plans, null);
        }

        //従属レコードの行を (宣言のパス, 行のモジュール, 行) で列挙する (子・孫も)
        IEnumerable<(string Path, ModuleDesign RowDesign, ModuleData Row)> OwnedRows(ModuleDesign design, ModuleData data, string prefix)
        {
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(design))
            {
                var path = EditHistoryPolicy.Path(prefix, owned.Name);
                var child = _designData.Modules.Find(owned.Condition.ModuleName);
                if (child == null || data.GetOwnedRows(owned.Name) is not { } rows) continue;
                foreach (var row in rows)
                {
                    yield return (path, child, row);
                    foreach (var e in OwnedRows(child, row, path)) yield return e;
                }
            }
        }

        static Plan RowPlan(int index, (ModuleDesign HistoryModule, EditHistoryContractFieldDesign Names, ModuleDesign Row) t, EditHistoryChangeType changeType)
            => new() { Index = index, Module = t.Row, HistoryModule = t.HistoryModule, Names = t.Names, ChangeType = changeType };

        //送信の中身が「親の版に含めない従属レコード (除外・行ごと)」の行 (とその子孫) だけか。親自身も、親の版に含める従属レコードも変わっていない
        bool OnlyNotIncludedRows(ModuleDesign module, ModuleSubmitData submitData)
        {
            var field = EditHistoryContracts.Field(module);
            if (field == null || (field.IndividuallyRecordedOwnedRecords.Count == 0 && field.ExcludedOwnedRecords.Count == 0)) return false;
            var rows = EditHistoryPolicy.NotIncludedModules(_designData, module, field);
            return submitData.SearchDelete.Count == 0
                && submitData.Add.All(e => rows.Contains(e.Name))
                && submitData.Update.All(e => rows.Contains(e.Name))
                && submitData.Delete.All(e => rows.Contains(e.ModuleName));
        }

        //ルートレコード自身が追加 (Add) か。画面の保存は仮 Id、一括取込 (ファイル / スクリプトの一括保存) は Id 空か手入力の Id で来る
        static bool IsRootAdd(ModuleSubmitData submitData)
            => submitData.Add.Any(e => e.Name == submitData.ModuleName && ModuleDataValues.GetId(e) == submitData.Id);

        //一括取込の自動採番の新規行は Id 空で来て、本体は結果 (DestinationId) に採番 Id を返さない。
        //画面の保存と同じく仮 Id を付けて送れば、本体の仮 Id 解決で採番 Id が結果に載り、保存後のレコードを読み直せる
        static void AssignTemporaryIdToRootAdd(ModuleSubmitData submitData)
        {
            if (!string.IsNullOrEmpty(submitData.Id)) return;
            var root = submitData.Add.FirstOrDefault(e => e.Name == submitData.ModuleName && string.IsNullOrEmpty(ModuleDataValues.GetId(e)));
            if (root == null) return;
            var tempId = IdFieldData.NewId();
            root.Fields[SystemFieldNames.Id] = tempId;
            submitData.Id = tempId.Value!;
        }

        //送信が履歴を持つモジュールのレコードに触るか (ルートのほか、追加・更新・削除する行のモジュール)
        bool TouchesHistoryModule(ModuleSubmitData submitData)
            => submitData.Add.Concat(submitData.Update).Select(e => e.Name).Concat(submitData.Delete.Select(e => e.ModuleName))
                .Any(name => EditHistoryContracts.Field(_designData.Modules.Find(name)) != null);

        static bool IsEmpty(ModuleSubmitData submitData)
            => submitData.Add.Count == 0 && submitData.Update.Count == 0 && submitData.Delete.Count == 0 && submitData.SearchDelete.Count == 0;

        static bool IsStandalone(ModuleDesign module, ModuleSubmitData submitData)
            => IsEmpty(submitData) && module.Fields.OfType<ExecuteSqlFieldDesign>().Any(e => e.Timing == ExecuteSqlTiming.Standalone);

        List<ModuleSubmitResult> Fail(List<ModuleSubmitData> transactionData, string message)
        {
            _logError?.Invoke(message);
            return transactionData.Select(e => new ModuleSubmitResult
            {
                SourceId = e.Id, DestinationId = e.Id, ExceptionMessage = message,
            }).ToList();
        }

        async Task WriteAsync(ModuleDataIOInternalAccess io, Plan plan, string id, string userId, DateTime localNow, DateTime utcNow)
        {
            var names = plan.Names;
            var data = new ModuleData { Name = plan.HistoryModule.Name };
            var set = CreateSetter(plan.HistoryModule, data);
            set(names.ModuleName, e => ((ValueFieldDataBase<string>)e).Value = plan.Module.Name);
            set(names.DataId, e => ((TextFieldData)e).Value = id);
            set(names.ChangeType, e => ((ValueFieldDataBase<string>)e).Value = plan.ChangeType.ToDesignValue());
            set(names.Snapshot, e => ((TextFieldData)e).Value = EditHistorySnapshot.Serialize(plan.Snapshot!));
            if (!string.IsNullOrEmpty(userId))
                set(names.UserId, e => ((ValueFieldDataBase<string>)e).Value = userId);
            //変更日時は本体の CreatedAt と同じく DateTime 役割のフィールドの SaveAsUtc に従う (サーバーローカル固定にしない)
            set(names.DateTime, e => ((DateTimeFieldData)e).Value = Truncate(
                plan.HistoryModule.Fields.FirstOrDefault(f => f.Name == names.DateTime) is DateTimeFieldDesign { SaveAsUtc: true } ? utcNow : localNow));
            await io.AddAsync(data);
        }

        //ミリ秒まで (本体の作成・更新日時と同じ精度)
        static DateTime Truncate(DateTime now)
            => new(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond);

        //役割のフィールド名が空・不在なら書かない (必須役割の不備はデザインチェックが指摘する)。型違いは例外 = 保存失敗
        static Action<string, Action<FieldDataBase>> CreateSetter(ModuleDesign design, ModuleData data)
            => (fieldName, setValue) =>
            {
                if (string.IsNullOrEmpty(fieldName)) return;
                var fieldDesign = design.Fields.FirstOrDefault(e => e.Name == fieldName);
                var fieldData = fieldDesign?.CreateData();
                if (fieldData == null) return;
                try
                {
                    setValue(fieldData);
                }
                catch (InvalidCastException)
                {
                    throw new InvalidOperationException(
                        $"Edit history field '{fieldName}' of '{design.Name}' has an unexpected type ({fieldData.GetType().Name}).");
                }
                data.Fields[fieldName] = fieldData;
            };

        record Plan
        {
            public int Index { get; init; }
            public ModuleDesign Module { get; init; } = null!;
            public ModuleDesign HistoryModule { get; init; } = null!;
            public EditHistoryContractFieldDesign Names { get; init; } = null!;
            public EditHistoryChangeType ChangeType { get; set; }
            public ModuleData? Snapshot { get; set; }
            public ModuleData? Before { get; set; }
            /// <summary>復活で Id が決まっている (論理削除・手入力 Id は元の Id、自動採番の物理削除は作り直した Id)。</summary>
            public string? FixedId { get; set; }
            /// <summary>行ごとに記録する行 (送信の Add): 送信の仮 Id。結果の対応表で採番 Id にする。</summary>
            public string? RowTempId { get; init; }
            /// <summary>行ごとに記録する行 (送信の Update / Delete): 行の Id。</summary>
            public string? RowId { get; init; }
        }
    }
}
