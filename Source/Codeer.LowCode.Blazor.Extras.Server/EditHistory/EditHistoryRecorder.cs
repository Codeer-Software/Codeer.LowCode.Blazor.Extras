using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataLogic;
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
    /// - 復活 / この版に置き換える: 送信に同梱された削除の取り消しの依頼を、本体の保存の前に処理する (EditHistoryUndeleter)。
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
        //設計に編集履歴 (EditHistoryField / 契約) が 1 つも無ければ、保存も読み出しも素通しにする (履歴を使わないアプリには何もしない)
        readonly bool _usesHistory;
        //行ごとに記録する先 (モジュール名 → 記録先)。設計から決まるので、1 回の保存の中でモジュールごとに 1 回だけ求める
        readonly Dictionary<string, (Dictionary<string, (ModuleDesign HistoryModule, EditHistoryContractFieldDesign Names, ModuleDesign Row)> Targets, string? Error)> _individualTargets = new();

        //親の版に含める従属レコードの持ち主 (行のモジュール名 → 持ち主)。設計から決まるので、使うときに 1 回だけ求める
        Dictionary<string, List<EditHistoryPolicy.IncludedOwner>>? _includedOwners;
        //行から親を引くときに、1 回の In で読む行の数 (DB のパラメータ数の上限に掛からない大きさ)
        const int OwnerSearchChunkSize = 500;
        static readonly string IdVariable = $"{SystemFieldNames.Id}.Value";
        //そのモジュールの従属レコードの行に、どれかの親の版に含まれる行があるか (モジュール名 → 有無)
        readonly Dictionary<string, bool> _ownsIncludedRows = new();

        /// <param name="logError">記録をスキップした理由・復活の失敗理由などを出すログ (ILogger の Warning 等)。</param>
        public EditHistoryRecorder(DesignData designData, Action<string>? logError = null)
        {
            _designData = designData;
            _logError = logError;
            _undeleter = new EditHistoryUndeleter(designData);
            _usesHistory = designData.Modules.ToList().Any(m => m.Fields.Any(f => f is EditHistoryFieldDesign or EditHistoryContractFieldDesign));
        }

        // ===== 読み出し: Snapshot を読む人の権限に落とす =====

        public async Task GetListAsync(ModuleDataIO io, SearchCondition condition, Paging<ModuleData> result)
        {
            if (!_usesHistory) return;
            var names = EditHistoryContracts.Contract(_designData.Modules.Find(condition.ModuleName));
            if (names == null || string.IsNullOrEmpty(names.Snapshot)) return;
            //検索・並びは DB の生のスナップショットに対して走るので、返る行の有無や順序から読めない列・行の値を推測できる。条件に使う読み出しは返さない
            if (UsesInCondition(condition, names.Snapshot))
                throw new InvalidOperationException($"'{names.Snapshot}' of the edit history module '{condition.ModuleName}' cannot be used in search or sort conditions.");
            foreach (var row in result.Items)
            {
                if (row.Fields.GetValueOrDefault(names.Snapshot) is not TextFieldData snapshotData || string.IsNullOrEmpty(snapshotData.Value)) continue;
                snapshotData.Value = await ToReadableAsync(io, snapshotData.Value);
            }
        }

        //検索条件 (絞り込みの対象) か並びにそのフィールドを使っているか
        static bool UsesInCondition(SearchCondition condition, string fieldName)
        {
#pragma warning disable CS0618 // 旧形式の並び指定 (SortFieldVariable) も見る
            var variables = condition.GetFieldValueConditions().Select(e => e.SearchTargetVariable)
                .Concat(condition.GetFieldVariableConditions().Select(e => e.SearchTargetVariable))
                .Concat(condition.SortConditions.Select(e => e.Variable))
                .Append(condition.SortFieldVariable);
#pragma warning restore CS0618
            return variables.Any(e => !string.IsNullOrEmpty(e) && new VariableName(e).FieldName.FullName == fieldName);
        }

        //スナップショット JSON を読む人の権限に落とす。壊れている・落とす途中で失敗・対象モジュールを読めない・行が条件に合わない、なら空 (版は出るが内容は見えない)
        async Task<string> ToReadableAsync(ModuleDataIO io, string json)
        {
            try
            {
                var snapshot = EditHistorySnapshot.Deserialize(json);
                if (snapshot == null || !await io.AuthorizationChecker.RestrictToReadableAsync(snapshot)) return string.Empty;
                return EditHistorySnapshot.Serialize(snapshot);
            }
            catch (Exception ex)
            {
                _logError?.Invoke($"Edit history snapshot could not be read: {ex.Message}");
                return string.Empty;
            }
        }

        // ===== 保存: 復活 → 本体の保存 → 記録 =====

        public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
        {
            if (!_usesHistory) return await next();
            var access = io.CreateSystemAccess();

            //履歴モジュールの版はシステムだけが書く (画面・API からの追加・更新は拒否。削除は古い版の整理のために許す)
            //見るのは送信に乗った行のモジュール (別のモジュールの送信に混ぜた行も同じ)
            foreach (var name in transactionData.SelectMany(e => e.Add.Concat(e.Update)).Select(e => e.Name).Distinct())
            {
                if (EditHistoryContracts.Contract(_designData.Modules.Find(name)) == null) continue;
                return Fail(transactionData, $"The edit history module '{name}' is written only by the system. Versions cannot be added or changed.");
            }

            //削除の取り消し (この版に置き換える / 復活ボタン)。本体の前に戻しておけば、同じ Submit の Update と一緒に確定する
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
                    //権限の拒否は型のまま外側 (監査ログのインターセプタ) へ上げる。本体の SubmitAsync が同じ形の結果にする
                    catch (Exception ex) when (ex is not LowCodeAccessDeniedException)
                    {
                        return Fail(transactionData, ex);
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
                if (resolved != null || TouchesHistoryModule(submitData)) context.KeepFilesOfDeletedRecords(submitData);
                if (resolved == null)
                {
                    //履歴を持たないモジュールの削除でも、一緒に消える従属レコードのうち自分の履歴を持つモジュールの行には削除の版を残す
                    if (isRootDelete && IndividualTargets(module, null, out var targetError).Count != 0)
                    {
                        if (targetError != null) return Fail(transactionData, targetError);
                        var full = await LoadFullAsync(access, module.Name, submitData.Id);
                        if (full != null)
                        {
                            var (rowPlans, rowError) = await IndividuallyRecordedDeletePlansAsync(access, i, module, full);
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
                    var full = await LoadFullAsync(access, module.Name, submitData.Id);
                    plan.Snapshot = EditHistoryPolicy.Strip(_designData, EditHistoryContracts.Field(module), full?.JsonClone());
                    if (full != null)
                    {
                        var (rowPlans, rowError) = await IndividuallyRecordedDeletePlansAsync(access, i, module, full);
                        if (rowError != null) return Fail(transactionData, rowError);
                        plans.AddRange(rowPlans);
                    }
                }
                else if (IsStandalone(module, submitData))
                {
                    //ExecuteSqlField (Standalone) だけの送信: レコード自体が変わったときだけ版にする (変更前を取っておいて後で比べる)
                    if (string.IsNullOrEmpty(submitData.Id) || IdFieldData.IsTemporaryId(submitData.Id)) continue;
                    plan.ChangeType = EditHistoryChangeType.Update;
                    plan.Before = await LoadAsync(access, module.Name, submitData.Id);
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
                    plans.Add(RowPlan(i, t, EditHistoryChangeType.Delete) with { RowId = d.Id, Snapshot = await LoadAsync(access, d.ModuleName, d.Id) });
                }
            }

            //親の版に含める従属レコードの行を、親の画面以外 (行のモジュールの画面・取込・他のモジュールの送信) で保存したときも、その行を含む親の版にする。
            //保存の前に、これから変わる・消える行が今入っている親を引いておく (保存の後では、消えた行・別の親へ付け替えた行の元の親が引けない)
            var owners = new HashSet<(string Module, string Id)>();
            var ownersError = await AddOwnersAsync(access, await RowsBeforeAsync(io, access, transactionData), owners);
            if (ownersError != null) return Fail(transactionData, ownersError);
            //保存の前に引いた親の、保存の前の内容。保存の後の内容と比べて、この保存で変わった親だけを版にする
            var baselines = new Dictionary<(string Module, string Id), string>();
            foreach (var owner in owners)
            {
                //この保存で自分の版を作るレコードは比べない (その版が 1 つできる)
                if (plans.Any(e => e.RowId == null && e.RowTempId == null && e.Module.Name == owner.Module && transactionData[e.Index].Id == owner.Id)) continue;
                var (target, targetError) = OwnerTarget(owner.Module);
                if (targetError != null) return Fail(transactionData, targetError);
                if (target == null) continue;
                if (await LoadAsync(access, owner.Module, owner.Id) is { } current) baselines[owner] = EditHistorySnapshot.Serialize(current);
            }
            foreach (var submitData in transactionData)
            {
                //行の追加は保存の後に採番 Id から親を引く。本体の一括 INSERT 経路は採番 Id が残らないので、1 行ずつの経路に落とす
                //(親と結び付く項目が空の行だけなら、どの親の版にもならないのでそのまま)
                if (submitData.Add.Any(HasOwnerValues)) submitData.NoTemporaryIdResolution = false;
            }

            var results = await next();
            if (results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage))) return results;
            //物理削除からの復活は作り直した Id をクライアントに返す (復活後の遷移先)
            foreach (var (index, restored) in restores)
            {
                if (restored.IsRecreated && index < results.Count) results[index].DestinationId = restored.RestoredId;
            }

            try
            {
                //保存の後は、この保存で追加・更新・取り消しした行が今入っている親を引く (本体が実際に書いた行。送信に乗らない内部書き込みも入る)
                var rowsAfter = context.ChangedRecords.Where(e => e.Kind != ChangedRecordKind.Delete).Select(e => (Module: e.ModuleName, e.Id)).ToHashSet();
                ownersError = await AddOwnersAsync(access, rowsAfter, owners);
                if (ownersError != null) throw new InvalidOperationException(ownersError);
                if (plans.Count == 0 && owners.Count == 0) return results;

                var user = await io.AuthorizationChecker.GetCurrentUserAsync();
                var userId = user == null ? string.Empty : ModuleDataValues.GetId(user);
                var localNow = DateTime.Now;
                var utcNow = DateTime.UtcNow;
                //この保存で版を扱ったレコード (親の画面からの保存など)。行から引いた親と重なったら、版は 1 つだけにする
                var recorded = new HashSet<(string Module, string Id)>();
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
                        plan.Snapshot = await LoadAsync(access, plan.Module.Name, id);
                    }
                    else if (plan.RowId != null)
                    {
                        id = plan.RowId;
                        if (plan.ChangeType != EditHistoryChangeType.Delete) plan.Snapshot = await LoadAsync(access, plan.Module.Name, id);
                    }
                    else if (plan.FixedId != null)
                    {
                        id = plan.FixedId;
                        plan.Snapshot = await LoadAsync(access, plan.Module.Name, id);
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
                        plan.Snapshot = await LoadAsync(access, plan.Module.Name, id);
                        //Id を変えた更新 (手入力 Id の書き換え・複合 Id の構成項目の変更): 旧 Id の版を新しい Id に付け替えて履歴を繋ぐ
                        var oldId = transactionData[plan.Index].Id;
                        if (plan.ChangeType == EditHistoryChangeType.Update && plan.Snapshot != null && oldId != id
                            && !string.IsNullOrEmpty(oldId) && !IdFieldData.IsTemporaryId(oldId))
                            await _undeleter.RelinkVersionsAsync(access, plan.Module, oldId, id);
                        //Standalone の SQL でレコードが変わっていなければ版にしない
                        if (plan.Before != null && plan.Snapshot != null &&
                            EditHistorySnapshot.Serialize(plan.Before) == EditHistorySnapshot.Serialize(plan.Snapshot)) continue;
                    }
                    recorded.Add((plan.Module.Name, id));
                    if (plan.Snapshot == null)
                    {
                        _logError?.Invoke($"Edit history of '{plan.Module.Name}' ({id}) was not recorded: the record could not be read.");
                        continue;
                    }
                    await WriteAsync(access, plan, id, userId, localNow, utcNow);
                }

                //行から引いた親: 保存の後の内容が保存の前と違うときだけ、更新の版にする (親が消えている・内容が変わっていないなら版にしない)
                foreach (var owner in owners)
                {
                    if (recorded.Contains(owner)) continue;
                    var (plan, error) = OwnerTarget(owner.Module);
                    if (error != null) throw new InvalidOperationException(error);
                    if (plan == null) continue;
                    plan.Snapshot = await LoadAsync(access, owner.Module, owner.Id);
                    if (plan.Snapshot == null) continue;
                    //保存の後に初めて引けた親 (行が新しく入った・付け替えで入ってきた) は、この保存で書いた行が親の内容に入っていれば変わっている
                    var changed = baselines.TryGetValue(owner, out var before)
                        ? before != EditHistorySnapshot.Serialize(plan.Snapshot)
                        : OwnedRows(plan.Module, plan.Snapshot, string.Empty).Any(e => rowsAfter.Contains((e.RowDesign.Name, EditHistorySnapshot.GetId(e.Row))));
                    if (!changed) continue;
                    await WriteAsync(access, plan, owner.Id, userId, localNow, utcNow);
                }
            }
            catch (Exception ex)
            {
                var message = $"Failed to record the edit history: {ex.Message}";
                _logError?.Invoke(message);
                foreach (var result in results)
                {
                    result.ExceptionMessage = message;
                    result.Exception = ex;
                }
            }
            return results;
        }

        // ===== 記録の元を読む =====

        //レコード + 従属レコード (明細・埋め込みモジュールの子レコード等、宣言の先) を内部読み (権限に関係なく全列・全従属) で読み、
        //NULL だった列も null として持たせる (版に「空だった」を残す = 復元で空に戻せる)。
        //含めない従属レコード (そのモジュールの EditHistoryField の除外・行ごと指定) は読まない
        async Task<ModuleData?> LoadAsync(ModuleDataIOSystemAccess io, string moduleName, string id)
        {
            var field = EditHistoryContracts.Field(_designData.Modules.Find(moduleName));
            return EditHistorySnapshot.FillNulls(_designData, await io.GetWithOwnedRecordsAsync(moduleName, id, path => EditHistoryPolicy.IsIncluded(field, path)));
        }

        //行ごとに記録する宣言の先も読む (除外は読まない)。親の削除で消える「行ごとに記録する行」を見つけるため
        async Task<ModuleData?> LoadFullAsync(ModuleDataIOSystemAccess io, string moduleName, string id)
        {
            var field = EditHistoryContracts.Field(_designData.Modules.Find(moduleName));
            return EditHistorySnapshot.FillNulls(_designData, await io.GetWithOwnedRecordsAsync(moduleName, id, path => !EditHistoryPolicy.IsExcluded(field, path)));
        }

        // ===== 行を含む親 (従属レコードの行を親の画面以外で保存したとき) =====

        Dictionary<string, List<EditHistoryPolicy.IncludedOwner>> IncludedOwners => _includedOwners ??= EditHistoryPolicy.IncludedOwners(_designData);

        //保存の前に親を引く行: 送信に乗った更新・削除の行、検索条件での削除で消える行、削除するレコードの従属レコードの行 (一緒に消える行)。
        //多めに拾ってよい (実際には変わらなかった行の親は、保存後の内容が最新の版と同じなので版にならない)
        async Task<List<(string Module, string Id)>> RowsBeforeAsync(ModuleDataIO userIo, ModuleDataIOSystemAccess io, List<ModuleSubmitData> transactionData)
        {
            var rows = new List<(string Module, string Id)>();
            if (IncludedOwners.Count == 0) return rows;
            foreach (var submitData in transactionData)
            {
                //送信のレコード自身 (その行が Update に無い・Id を変える更新で行の Id が差し替えの形でも、送信の Id で引く)
                rows.Add((submitData.ModuleName, submitData.Id));
                rows.AddRange(submitData.Update.Select(e => (e.Name, ModuleDataValues.GetId(e))));

                var deletes = submitData.Delete.Select(e => (Module: e.ModuleName, e.Id)).ToList();
                foreach (var condition in submitData.SearchDelete)
                {
                    //条件の無い検索削除は本体が拒否する
                    if (condition.Condition == null || !TouchesIncludedRows(condition.ModuleName)) continue;
                    var copy = condition.JsonClone();
                    copy.SelectFields = new List<string> { SystemFieldNames.Id };
                    //本体の検索削除と同じく、利用者の権限で読む (読めない要求はここで拒否される)
                    deletes.AddRange((await userIo.GetListAsync(copy, 0)).Items.Select(e => (condition.ModuleName, ModuleDataValues.GetId(e))));
                }
                foreach (var (moduleName, id) in deletes)
                {
                    rows.Add((moduleName, id));
                    var design = _designData.Modules.Find(moduleName);
                    if (design == null || !OwnsIncludedRows(design) || !IsStoredId(id)) continue;
                    var full = await io.GetWithOwnedRecordsAsync(moduleName, id);
                    if (full == null) continue;
                    rows.AddRange(OwnedRows(design, full, string.Empty).Select(e => (e.RowDesign.Name, EditHistorySnapshot.GetId(e.Row))));
                }
            }
            return rows;
        }

        //そのモジュールの行か、その従属レコードの行 (子・孫) が、どれかの親の版に含まれるか
        bool TouchesIncludedRows(string moduleName)
            => IncludedOwners.ContainsKey(moduleName) || (_designData.Modules.Find(moduleName) is { } design && OwnsIncludedRows(design));

        //そのモジュールの従属レコードの行 (子・孫) に、どれかの親の版に含まれるモジュールの行があるか (設計から決まる)
        bool OwnsIncludedRows(ModuleDesign design)
        {
            if (!_ownsIncludedRows.TryGetValue(design.Name, out var owns))
            {
                owns = EditHistoryPolicy.Walk(_designData, design, null, descendIntoNotIncluded: true).Any(e => e.Child != null && IncludedOwners.ContainsKey(e.Child.Name));
                _ownsIncludedRows[design.Name] = owns;
            }
            return owns;
        }

        //DB にある行の Id か (空・仮 Id・差し替えの形のままの Id は、まだその Id の行が無い)
        static bool IsStoredId(string id) => !string.IsNullOrEmpty(id) && !id.StartsWith('@');

        //行 (モジュール名, Id) を版に含める親のレコードを owners に足す。孫の行は上の親までたどる (自己参照の従属は祖先すべて)。
        //親は宣言の結び付き (行の項目 = 親の項目) を使って、行の値から引く。フィールドの型は見ない。
        //宣言の条件のうち結び付き以外 (行の絞り込み) は見ないので、親の一部でない行の親も入る (保存後の内容が最新の版と同じなので版にはならない)。
        //行のモジュールを版に含める親 (履歴を持つモジュール) が 2 つ以上ある設計は、どちらの版にするか決まらないのでエラー (戻り値が理由)
        async Task<string?> AddOwnersAsync(ModuleDataIOSystemAccess io, IEnumerable<(string Module, string Id)> rows, HashSet<(string Module, string Id)> owners)
        {
            var visited = new HashSet<(string, string)>();
            var pending = rows.ToList();
            while (pending.Count != 0)
            {
                var next = new List<(string Module, string Id)>();
                foreach (var group in pending.GroupBy(e => e.Module))
                {
                    if (!IncludedOwners.TryGetValue(group.Key, out var found)) continue;
                    if (EditHistoryPolicy.HasMultipleRoots(found))
                    {
                        var other = found.First(e => e.Root.Name != found[0].Root.Name);
                        return $"The rows of module '{group.Key}' are included in the versions of more than one parent ('{found[0].Path}' of '{found[0].Root.Name}' and '{other.Path}' of '{other.Root.Name}'). The edit history cannot decide which parent's version to record.";
                    }
                    var ids = group.Select(e => e.Id).Where(e => IsStoredId(e) && visited.Add((group.Key, e))).ToList();
                    //読むのは結び付きに使う行の項目だけ
                    var fields = found.SelectMany(e => e.Bindings).Select(e => new VariableName(e.SearchTargetVariable).FieldName.FullName)
                        .Append(SystemFieldNames.Id).Distinct().ToList();
                    foreach (var chunk in ids.Chunk(OwnerSearchChunkSize))
                    {
                        var loaded = await ReadRowsAsync(io, group.Key, chunk, fields);
                        //同じ親の中に持ち主が複数あれば (明細と付属品が同じモジュール等)、それぞれの宣言で親を引く
                        foreach (var owner in found)
                        {
                            foreach (var parentId in await FindParentIdsAsync(io, owner, loaded))
                            {
                                if (owner.Owner.Name == owner.Root.Name) owners.Add((owner.Root.Name, parentId));
                                next.Add((owner.Owner.Name, parentId));
                            }
                        }
                    }
                }
                pending = next;
            }
            return null;
        }

        //行を Id で読む (結び付きに使う項目だけ)。複合 Id のモジュールは Id を条件に書けないので 1 行ずつ読む
        async Task<List<ModuleData>> ReadRowsAsync(ModuleDataIOSystemAccess io, string moduleName, string[] ids, List<string> fields)
        {
            var idField = _designData.Modules.Find(moduleName)?.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id);
            if (idField != null && idField.CompositeIdVariables.Count != 0)
            {
                var rows = new List<ModuleData>();
                foreach (var id in ids)
                {
                    if (await io.GetWithOwnedRecordsAsync(moduleName, id, _ => false) is { } row) rows.Add(row);
                }
                return rows;
            }
            var condition = new SearchCondition
            {
                ModuleName = moduleName,
                Condition = new FieldValueMatchCondition { SearchTargetVariable = IdVariable, Comparison = MatchComparison.In, Value = MultiTypeValue.Create(ids.ToList()) },
                SelectFields = fields,
            };
            return (await io.GetListAsync(condition)).Items;
        }

        //行の値から、その宣言で行を持つ親のレコードの Id を引く
        static async Task<HashSet<string>> FindParentIdsAsync(ModuleDataIOSystemAccess io, EditHistoryPolicy.IncludedOwner owner, List<ModuleData> rows)
        {
            var result = new HashSet<string>();
            //行ごとの、親と結び付く項目の値 (空の項目がある行は、どの親にも入っていない)
            var keys = rows
                .Select(row => owner.Bindings.Select(e => row.TryGetVariableValue(e.SearchTargetVariable, out var value) ? value : null).ToList())
                .Where(values => values.All(e => !string.IsNullOrEmpty(e?.ToString())))
                .DistinctBy(values => string.Join('\u001f', values)).ToList();
            if (keys.Count == 0) return result;

            //親の Id で結び付く宣言 (明細が親の Id を持つ形) は、行の値がそのまま親の Id
            if (owner.Bindings.Count == 1 && owner.Bindings[0].Variable == IdVariable)
            {
                foreach (var values in keys) result.Add(values[0]!.ToString()!);
                return result;
            }
            //それ以外は親を検索する。結び付きが 1 項目ならまとめて、複数なら値の組ごとに
            var conditions = owner.Bindings.Count == 1
                ? new List<MatchConditionBase>
                {
                    new FieldValueMatchCondition
                    {
                        SearchTargetVariable = owner.Bindings[0].Variable, Comparison = MatchComparison.In, Value = MultiTypeValue.Create(keys.Select(e => e[0]).ToList()),
                    },
                }
                : keys.Select(values => MultiMatchCondition.And(owner.Bindings.Select((e, i) => (MatchConditionBase)new FieldValueMatchCondition
                {
                    SearchTargetVariable = e.Variable, Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(values[i]),
                }).ToArray())).ToList();
            foreach (var condition in conditions)
            {
                var search = new SearchCondition { ModuleName = owner.Owner.Name, Condition = condition, SelectFields = new List<string> { SystemFieldNames.Id } };
                foreach (var parent in (await io.GetListAsync(search)).Items)
                {
                    var parentId = ModuleDataValues.GetId(parent);
                    if (!string.IsNullOrEmpty(parentId)) result.Add(parentId);
                }
            }
            return result;
        }

        //追加する行が、どれかの親と結び付く値を持っているか (持っていなければ、どの親の版にもならない)
        bool HasOwnerValues(ModuleData row)
            => IncludedOwners.TryGetValue(row.Name, out var found)
                && found.Any(owner => owner.Bindings.All(e => row.TryGetVariableValue(e.SearchTargetVariable, out var value) && !string.IsNullOrEmpty(value?.ToString())));

        //行から引いた親の版の書き先 (履歴モジュール・契約)。履歴を持たないモジュールなら null
        (Plan? Plan, string? Error) OwnerTarget(string moduleName)
        {
            var module = _designData.Modules.Find(moduleName);
            if (module == null) return (null, null);
            var resolved = EditHistoryContracts.Resolve(_designData, module, out var error);
            if (error != null || resolved == null) return (null, error);
            return (new Plan { Module = module, HistoryModule = resolved.Value.HistoryModule, Names = resolved.Value.Names, ChangeType = EditHistoryChangeType.Update }, null);
        }

        //行ごとに記録する宣言の先のモジュール → 記録先 (履歴モジュール・契約)。
        //親に EditHistoryField があれば IndividuallyRecordedOwnedRecords の宣言の先。
        //親に EditHistoryField が無ければ (親に版が無い)、従属レコードのうち自分の EditHistoryField を持つモジュール全部 = 子は自分の履歴に行ごとに残す
        Dictionary<string, (ModuleDesign HistoryModule, EditHistoryContractFieldDesign Names, ModuleDesign Row)> IndividualTargets(ModuleDesign module, EditHistoryFieldDesign? field, out string? error)
        {
            if (!_individualTargets.TryGetValue(module.Name, out var cached))
            {
                var targets = FindIndividualTargets(module, field, out var found);
                _individualTargets[module.Name] = cached = (targets, found);
            }
            error = cached.Error;
            return cached.Targets;
        }

        Dictionary<string, (ModuleDesign HistoryModule, EditHistoryContractFieldDesign Names, ModuleDesign Row)> FindIndividualTargets(ModuleDesign module, EditHistoryFieldDesign? field, out string? error)
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
        async Task<(List<Plan> Plans, string? Error)> IndividuallyRecordedDeletePlansAsync(ModuleDataIOSystemAccess io, int index, ModuleDesign module, ModuleData full)
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
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(_designData.Modules, design))
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

        //送信が履歴を持つモジュールのレコードか、どれかの親の版に含まれる行に触るか (ルートのほか、追加・更新・削除する行のモジュール)
        bool TouchesHistoryModule(ModuleSubmitData submitData)
            => submitData.Add.Concat(submitData.Update).Select(e => e.Name).Concat(submitData.Delete.Select(e => e.ModuleName))
                .Any(name => EditHistoryContracts.Field(_designData.Modules.Find(name)) != null || IncludedOwners.ContainsKey(name));

        static bool IsEmpty(ModuleSubmitData submitData)
            => submitData.Add.Count == 0 && submitData.Update.Count == 0 && submitData.Delete.Count == 0 && submitData.SearchDelete.Count == 0;

        static bool IsStandalone(ModuleDesign module, ModuleSubmitData submitData)
            => IsEmpty(submitData) && module.Fields.OfType<ExecuteSqlFieldDesign>().Any(e => e.Timing == ExecuteSqlTiming.Standalone);

        //例外による失敗。ホストが利用者に返す文言を例外の型で決められるように Exception も結果に入れる
        List<ModuleSubmitResult> Fail(List<ModuleSubmitData> transactionData, Exception ex)
        {
            var results = Fail(transactionData, ex.Message);
            foreach (var result in results) result.Exception = ex;
            return results;
        }

        List<ModuleSubmitResult> Fail(List<ModuleSubmitData> transactionData, string message)
        {
            _logError?.Invoke(message);
            return transactionData.Select(e => new ModuleSubmitResult
            {
                SourceId = e.Id, DestinationId = e.Id, ExceptionMessage = message,
            }).ToList();
        }

        async Task WriteAsync(ModuleDataIOSystemAccess io, Plan plan, string id, string userId, DateTime localNow, DateTime utcNow)
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
