using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server.EditHistory
{
    /// <summary>
    /// 編集履歴の記録。ホストの ModuleDataIO の SubmitAsync を包んで、EditHistoryField を置いたモジュールの
    /// ルートレコード 1 件につき履歴モジュールに 1 行 (レコード全体のスナップショット) を書く。
    /// - 削除: base の前に削除前の内容を読み、base 成功後に ChangeType=Delete で書く
    /// - 作成・更新: base 成功後に保存後の内容 (明細を含む) を DB から読み直して書く
    /// 履歴の書き込みは内部 add 経路 (操作ユーザーの書き込み権限に依存しない)。
    /// 読み直しは操作ユーザーの権限で行うため、読めない列はスナップショットに入らない。
    /// </summary>
    /// <remarks>
    /// 履歴の記録に失敗したときは結果に ExceptionMessage を立てて保存ごと失敗 (ロールバック) にする
    /// (履歴が静かに欠けるより、保存できないことがユーザーに見える方がよい)。
    /// EditHistoryField があるのに履歴モジュール・契約が無い設計 (デザインチェックが指摘する不備) も同様に失敗にする。
    /// 一括取込 (ファイル / スクリプトの一括保存) も記録する。Id 空の新規行には仮 Id を付けて採番 Id を引く。
    /// 履歴対象モジュールの投入は本体の一括 INSERT 経路 (BulkAddThreshold 以上の純追加・採番 Id が返らない) を使わず、
    /// 1 行ずつの経路に落とす (NoTemporaryIdResolution を外す)。
    /// </remarks>
    public class EditHistoryRecorder
    {
        const string TemporaryIdPrefix = "@temporary:";

        readonly DesignData _designData;
        readonly ModuleDataIO _io;
        readonly Func<ModuleData, Task<string>> _addInternalAsync;
        readonly Action<string>? _logError;

        /// <param name="io">操作ユーザーのモジュールデータ IO (スナップショットの読み直しと現在ユーザーの取得に使う)。</param>
        /// <param name="addInternalAsync">内部の追加経路 (テンプレートの CustomizedModuleDataIO.AddSystemRecordAsync)。</param>
        public EditHistoryRecorder(DesignData designData, ModuleDataIO io,
            Func<ModuleData, Task<string>> addInternalAsync, Action<string>? logError = null)
        {
            _designData = designData;
            _io = io;
            _addInternalAsync = addInternalAsync;
            _logError = logError;
        }

        /// <summary>
        /// SubmitAsync の override から呼ぶ。submitAsync (base.SubmitAsync) の前後で履歴を記録する。
        /// </summary>
        public async Task<List<ModuleSubmitResult>> SubmitAsync(List<ModuleSubmitData> transactionData,
            Func<Task<List<ModuleSubmitResult>>> submitAsync)
        {
            //論理削除の取り消し (復元で Id を保って戻した行・復活ボタン)。base の前に戻しておけば、同じ Submit の Update と一緒に確定する
            var restoredRoots = new HashSet<int>();
            for (var i = 0; i < transactionData.Count; i++)
            {
                var submitData = transactionData[i];
                var targets = submitData.ExtendedData.OfType<EditHistoryUndeleteData>().SelectMany(e => e.Targets).ToList();
                if (targets.Count == 0) continue;
                try
                {
                    foreach (var target in targets) await _io.UndeleteAsync(target.ModuleName, target.Id);
                }
                catch (Exception ex)
                {
                    return Fail(transactionData, ex.Message);
                }
                if (targets.Any(e => e.ModuleName == submitData.ModuleName && e.Id == submitData.Id)) restoredRoots.Add(i);
            }

            var plans = new List<Plan>();
            for (var i = 0; i < transactionData.Count; i++)
            {
                var submitData = transactionData[i];
                var module = _designData.Modules.Find(submitData.ModuleName);
                if (module == null) continue;
                var resolved = EditHistoryContracts.Resolve(_designData, module, out var error);
                if (error != null) return Fail(transactionData, error);
                if (resolved == null) continue;
                //履歴対象の投入は結果の仮 Id 解決を使う (= 本体の一括 INSERT 経路の対象外にし、1 行ずつの経路で採番 Id を得る)
                submitData.NoTemporaryIdResolution = false;
                AssignTemporaryIdToRootAdd(submitData);

                var plan = new Plan
                {
                    Index = i, Module = module, HistoryModule = resolved.Value.HistoryModule, Names = resolved.Value.Names,
                };
                var isRootDelete = submitData.Delete.Any(e => e.ModuleName == submitData.ModuleName && e.Id == submitData.Id);
                if (restoredRoots.Contains(i))
                {
                    plan.ChangeType = EditHistoryChangeType.Restore;
                }
                else if (isRootDelete)
                {
                    plan.ChangeType = EditHistoryChangeType.Delete;
                    plan.Snapshot = await LoadAsync(module.Name, submitData.Id);
                }
                else if (IsStandalone(module, submitData))
                {
                    //ExecuteSqlField (Standalone) だけの送信: レコード自体が変わったときだけ版にする (変更前を取っておいて後で比べる)
                    if (string.IsNullOrEmpty(submitData.Id) || submitData.Id.StartsWith(TemporaryIdPrefix)) continue;
                    plan.ChangeType = EditHistoryChangeType.Update;
                    plan.Before = await LoadAsync(module.Name, submitData.Id);
                }
                else if (IsEmpty(submitData))
                {
                    //何も保存しない送信 (承認の申請で申請書に変更が無いときなど。base は何も書かない) は版にしない
                    continue;
                }
                else
                {
                    plan.ChangeType = IsRootAdd(submitData) ? EditHistoryChangeType.Add : EditHistoryChangeType.Update;
                }
                plans.Add(plan);
            }

            var results = await submitAsync();
            if (plans.Count == 0 || results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage))) return results;

            try
            {
                var user = await _io.GetCurrentUser();
                var userId = user == null ? string.Empty : ModuleDataValues.GetId(user);
                var now = DateTime.Now;
                foreach (var plan in plans)
                {
                    string id;
                    if (plan.ChangeType == EditHistoryChangeType.Delete)
                    {
                        id = transactionData[plan.Index].Id;
                    }
                    else
                    {
                        id = plan.Index < results.Count ? results[plan.Index].DestinationId : string.Empty;
                        if (string.IsNullOrEmpty(id) || id.StartsWith(TemporaryIdPrefix))
                        {
                            _logError?.Invoke($"Edit history of '{plan.Module.Name}' was not recorded: the Id of the saved record is not available (bulk insert, or an ExecuteSqlField create without NewId).");
                            continue;
                        }
                        plan.Snapshot = await LoadAsync(plan.Module.Name, id);
                        //Standalone の SQL でレコードが変わっていなければ版にしない
                        if (plan.Before != null && plan.Snapshot != null &&
                            EditHistorySnapshot.Serialize(plan.Before) == EditHistorySnapshot.Serialize(plan.Snapshot)) continue;
                    }
                    if (plan.Snapshot == null)
                    {
                        _logError?.Invoke($"Edit history of '{plan.Module.Name}' ({id}) was not recorded: the record could not be read.");
                        continue;
                    }
                    //受け取った操作 (契約に Command 役割があるときだけ)。仮 Id は結果の対応表で実 Id にする
                    var command = string.IsNullOrEmpty(plan.Names.Command) ? null
                        : EditHistoryCommand.Serialize(transactionData[plan.Index], plan.Index < results.Count ? results[plan.Index].TemporaryIdMap : null);
                    await WriteAsync(plan, id, userId, now, command);
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

        //Add / Update / Delete が無く、モジュールに Standalone の ExecuteSqlField があるとき base は Standalone の SQL だけを実行する
        //レコード + 従属レコードを読み、NULL だった列も null として持たせる (版に「空だった」を残す = 復元で空に戻せる)
        async Task<ModuleData?> LoadAsync(string moduleName, string id)
            => EditHistorySnapshot.FillNulls(_designData, await _io.GetWithOwnedRecordsAsync(moduleName, id));

        //ルートレコード自身が追加 (Add) か。画面の保存は仮 Id、一括取込 (ファイル / スクリプトの一括保存) は Id 空か手入力の Id で来る
        static bool IsRootAdd(ModuleSubmitData submitData)
            => submitData.Add.Any(e => e.Name == submitData.ModuleName && ModuleDataValues.GetId(e) == submitData.Id);

        //一括取込の自動採番の新規行は Id 空で来て、base は結果 (DestinationId) に採番 Id を返さない。
        //画面の保存と同じく仮 Id を付けて送れば、base の仮 Id 解決で採番 Id が結果に載り、保存後のレコードを読み直せる
        static void AssignTemporaryIdToRootAdd(ModuleSubmitData submitData)
        {
            if (!string.IsNullOrEmpty(submitData.Id)) return;
            var root = submitData.Add.FirstOrDefault(e => e.Name == submitData.ModuleName && string.IsNullOrEmpty(ModuleDataValues.GetId(e)));
            if (root == null) return;
            var tempId = IdFieldData.NewId();
            root.Fields[SystemFieldNames.Id] = tempId;
            submitData.Id = tempId.Value!;
        }

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

        async Task WriteAsync(Plan plan, string id, string userId, DateTime now, string? command)
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
            set(names.DateTime, e => ((DateTimeFieldData)e).Value = now);
            if (command != null)
                set(names.Command, e => ((TextFieldData)e).Value = command);
            await _addInternalAsync(data);
        }

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

        class Plan
        {
            public int Index;
            public ModuleDesign Module = null!;
            public ModuleDesign HistoryModule = null!;
            public EditHistoryContractFieldDesign Names = null!;
            public EditHistoryChangeType ChangeType;
            public ModuleData? Snapshot;
            public ModuleData? Before;
        }
    }
}
