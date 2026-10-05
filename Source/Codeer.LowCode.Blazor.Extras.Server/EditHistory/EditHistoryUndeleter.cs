using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server.EditHistory
{
    /// <summary>削除の取り消し (復活) の結果。</summary>
    sealed class EditHistoryUndeleteResult
    {
        /// <summary>レコード全体を戻したか (復活ボタン)。false は「この版に戻す」の行の取り消し。</summary>
        public bool IsWholeRecord { get; init; }
        /// <summary>新しい Id で作り直したか (自動採番の物理削除)。</summary>
        public bool IsRecreated { get; init; }
        public string RestoredId { get; init; } = string.Empty;
    }

    /// <summary>
    /// 削除の取り消し (復活)。クライアントが送る「履歴行の Id」から版のスナップショットを読み、レコードを戻す。
    /// EditHistoryRecorder が本体の保存の前に呼ぶ (同じトランザクション)。
    /// - 復活ボタン: 削除の版からレコード全体を戻す。戻せるのはそのレコードの最新の版が削除で、今そのレコードが無いときだけ。
    ///   論理削除は Id を保って取り消し (親と一緒に消えた従属レコードも)、物理削除は作り直す (自動採番は新しい Id・手入力 Id は元の Id)。
    ///   新しい Id で作り直したときは、履歴を持つモジュールの旧 Id の版を新しい Id に付け替えて履歴を繋ぐ (履歴の行を書き換える唯一の箇所)。
    /// - この版に戻す: 版の従属レコードのうち「今は削除中で、その人に見えている」行だけを Id を保って取り消す。
    /// 権限は削除と同じ (CanDelete と UserRead / UserWrite 条件、行の条件)。戻せなければ例外 (= 保存失敗)。
    /// </summary>
    sealed class EditHistoryUndeleter(DesignData designData)
    {
        //履歴行を読み、版のスナップショットから戻す
        internal async Task<EditHistoryUndeleteResult> RestoreAsync(ModuleDataIOInternalAccess io, ModuleSubmitData submitData, EditHistoryUndeleteData request)
        {
            var historyModule = designData.Modules.Find(request.HistoryModuleName)
                ?? throw new InvalidOperationException($"Edit history module '{request.HistoryModuleName}' does not exist.");
            var names = EditHistoryContracts.Contract(historyModule)
                ?? throw new InvalidOperationException($"Edit history module '{historyModule.Name}' has no {nameof(EditHistoryContractFieldDesign)}.");
            var historyRow = await io.GetWithOwnedRecordsAsync(historyModule.Name, request.HistoryRowId, _ => false)
                ?? throw new InvalidOperationException("The edit history version does not exist.");
            var targetModuleName = EditHistoryContracts.GetText(historyRow, names.ModuleName);
            var dataId = EditHistoryContracts.GetText(historyRow, names.DataId);
            var snapshot = EditHistorySnapshot.Deserialize(EditHistoryContracts.GetText(historyRow, names.Snapshot));
            //版は送信の対象レコードのものに限る (別のレコードの版を指定して他人の行を戻せないように)
            if (snapshot == null || targetModuleName != submitData.ModuleName || dataId != submitData.Id || snapshot.Name != submitData.ModuleName)
                throw new InvalidOperationException("The edit history version does not belong to the record being saved.");
            var module = designData.Modules.Find(targetModuleName)
                ?? throw new InvalidOperationException($"Module '{targetModuleName}' does not exist.");
            var field = EditHistoryContracts.Field(module);
            if (field?.CanRestore == false)
                throw new InvalidOperationException($"Restoring from the edit history is disabled for '{module.Name}'.");

            if (!request.RestoreWholeRecord)
            {
                //この版に戻す: 版の従属レコードのうち「今は削除中で、その人に見えている (差分に出ている)」論理削除の行を Id を保って取り消す
                //(レコード自身と行の値はフォームの保存が持つ)。見えていない行は触らない。戻せない行 (削除権限が無い等) があれば例外 = 保存失敗
                var visible = snapshot.JsonClone();
                if (await io.RemoveUnreadableAsync(visible)) await UndeleteDeletedRowsAsync(io, module, field, visible, string.Empty, together: false);
                return new EditHistoryUndeleteResult { IsWholeRecord = false, RestoredId = dataId };
            }

            //復活ボタン: 削除の版からレコード全体を戻す。復活できるのはそのレコードの最新の版が削除のときだけ (復活済み・作り直し済みなら不可)
            if (EditHistoryContracts.GetText(historyRow, names.ChangeType) != EditHistoryChangeType.Delete.ToString())
                throw new InvalidOperationException("Only a deleted record (a Delete version) can be restored.");
            var latest = (await io.GetListAsync(EditHistoryContracts.VersionsCondition(historyModule.Name, names, targetModuleName, dataId, 1, SystemFieldNames.Id))).Items.FirstOrDefault();
            if (latest == null || EditHistorySnapshot.GetId(latest) != request.HistoryRowId)
                throw new InvalidOperationException("Only the latest version can be restored. The record has been changed or restored after this version.");
            //復活できるのは今そのレコードが無い (削除中) ときだけ。版の削除で Delete 版が最新に戻った場合や別の経路で戻っていた場合に、
            //同じレコードをもう 1 件作ったり、削除されていないレコードに復活の版を積んだりしない (手入力 Id で同じ Id のレコードがある場合も同じ)
            if (await io.GetWithOwnedRecordsAsync(module.Name, dataId, _ => false) != null)
                throw new InvalidOperationException($"The record '{dataId}' of '{module.Name}' exists (it is not deleted). Only a deleted record can be restored.");

            if (module.UsesLogicalDelete())
            {
                //論理削除: Id を保って取り消し (権限は削除と同じ)。親と一緒に消えた従属レコードも戻す (親の削除が子を消すのと同じ規則)
                await io.UndeleteAsync(module.Name, dataId, snapshot);
                await UndeleteDeletedRowsAsync(io, module, field, snapshot, string.Empty, together: true);
                return new EditHistoryUndeleteResult { IsWholeRecord = true, RestoredId = dataId };
            }
            //物理削除: スナップショットから作り直す。手入力 Id は元の Id、自動採番は新しい Id (旧 Id の版を付け替えて履歴を繋ぐ)
            if (!await io.CanUndeleteAsync(module.Name, snapshot)) throw new InvalidOperationException("You are not allowed to restore this record.");
            var newId = await RecreateAsync(io, module, field, snapshot, string.Empty, null, null);
            return new EditHistoryUndeleteResult { IsWholeRecord = true, IsRecreated = newId != dataId, RestoredId = newId };
        }

        //版の従属レコード (含める宣言の先。子・孫も) のうち、今は削除中 (通常の読み出しで見つからない) の行を戻す。
        //論理削除モジュールの行は Id を保って取り消す。together = 親と一緒に消えた行の取り消し (子の CanDelete だけ見る。物理削除モジュールの行は作り直す)。
        //together でなければ (この版に戻す) その人の権限で取り消す (物理削除モジュールの行はフォームが新しい行として送る)
        async Task UndeleteDeletedRowsAsync(ModuleDataIOInternalAccess io, ModuleDesign design, EditHistoryFieldDesign? field, ModuleData data, string prefix, bool together)
        {
            var recreated = new Dictionary<string, Dictionary<string, string>>();
            foreach (var owned in OrderByReferences(design))
            {
                var path = EditHistoryPolicy.Path(prefix, owned.Name);
                if (!EditHistoryPolicy.IsIncluded(field, path)) continue;
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                if (child == null || data.GetOwnedRows(owned.Name) is not { } rows) continue;
                //親が子を参照する宣言 (埋め込みモジュール) の子は親の削除で消えない (親と一緒に戻すものではない)
                var binding = GetBinding(owned);
                foreach (var row in rows)
                {
                    var id = EditHistorySnapshot.GetId(row);
                    var exists = id.Length == 0 || await io.GetWithOwnedRecordsAsync(child.Name, id, _ => false) != null;
                    if (!exists && child.UsesLogicalDelete())
                    {
                        if (together) await io.UndeleteTogetherAsync(child.Name, id);
                        else await io.UndeleteAsync(child.Name, id, row);
                    }
                    else if (!exists && together && binding is { ParentRefersChild: false })
                    {
                        //物理削除モジュールの明細は親の削除で消えている。作り直す (親の Id は変わらないので参照はそのまま)
                        if (!child.CanDelete) throw new InvalidOperationException($"The rows of '{child.Name}' cannot be restored.");
                        var newId = await RecreateAsync(io, child, field, ResolveReferences(owned, row, recreated), path, owned, EditHistorySnapshot.GetId(data));
                        Remember(recreated, owned.Name, id, newId);
                        continue;
                    }
                    await UndeleteDeletedRowsAsync(io, child, field, row, path, together);
                }
            }
        }

        //スナップショットの行を新しいレコードとして作る (自動採番は Id 振り直し・手入力 Id は元の Id)。従属レコードの行も作り直す。戻り値は新しい Id。
        //親への参照は宣言の束縛条件から付け替える: 子が親を参照する宣言 (明細) は親を先に作って子の参照を親の新しい Id に、
        //親が子を参照する宣言 (埋め込みモジュール) は子を先に用意して親の参照を子の Id にする (子が今もあればそれを参照し、無ければ作り直す)。
        //履歴を持つモジュールのレコードを新しい Id で作ったときは、旧 Id の版を新しい Id に付け替える (履歴が繋がる・旧 Id の削除の版から二度復活できない)
        async Task<string> RecreateAsync(ModuleDataIOInternalAccess io, ModuleDesign design, EditHistoryFieldDesign? field, ModuleData data, string prefix,
            OwnedRecordsDesign? bindTo, string? parentId)
        {
            var oldId = EditHistorySnapshot.GetId(data);
            var keepId = IsManualId(design);
            var copy = data.JsonClone();
            //従属レコードの行 (一覧・内容を持つ埋め込みの子) は外す (下で作り直して参照を付ける)。内容を持たない参照だけの項目はそのまま写す
            foreach (var key in copy.Fields.Keys.Where(e => (EditHistoryContracts.IsExcludedField(e) && !(keepId && e == SystemFieldNames.Id)) || EditHistoryContracts.HasOwnedRows(copy.Fields[e])).ToList())
                copy.Fields.Remove(key);

            //親が子を参照する宣言 (埋め込みモジュール): 子を先に用意し、自分の参照を子の Id にする
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(design))
            {
                var path = EditHistoryPolicy.Path(prefix, owned.Name);
                var binding = GetBinding(owned);
                if (binding is not { ParentRefersChild: true } || !EditHistoryPolicy.IsIncluded(field, path)) continue;
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                if (child == null || data.GetOwnedRows(owned.Name)?.FirstOrDefault() is not { } row) continue;
                var childId = EditHistorySnapshot.GetId(row);
                if (childId.Length == 0 || await io.GetWithOwnedRecordsAsync(child.Name, childId, _ => false) == null)
                {
                    if (!child.CanDelete) throw new InvalidOperationException($"The record of '{child.Name}' cannot be restored.");
                    childId = await RecreateAsync(io, child, field, row, path, null, null);
                }
                SetReference(design, copy, binding.ParentField, childId);
            }
            //子が親を参照する宣言 (明細) の子として作るとき: 参照を親の新しい Id にする
            if (bindTo != null && parentId != null && GetBinding(bindTo) is { ParentRefersChild: false } toParent)
                SetReference(design, copy, toParent.ChildField, parentId);

            var newId = await io.AddAsync(copy);
            if (oldId.Length != 0 && newId != oldId) await RelinkVersionsAsync(io, design, oldId, newId);

            //子が親を参照する宣言 (明細) の子を、親の新しい Id で作り直す (親と一緒に消えた行なので子の CanDelete だけ見る)。
            //他の従属レコード群の行を指す項目 (宣言の References) は、指す先の行を作り直した Id に付け替える (指される側を先に作る)
            var recreated = new Dictionary<string, Dictionary<string, string>>();
            foreach (var owned in OrderByReferences(design))
            {
                var path = EditHistoryPolicy.Path(prefix, owned.Name);
                if (GetBinding(owned) is not { ParentRefersChild: false } || !EditHistoryPolicy.IsIncluded(field, path)) continue;
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                if (child == null || data.GetOwnedRows(owned.Name) is not { } rows) continue;
                if (rows.Count != 0 && !child.CanDelete) throw new InvalidOperationException($"The rows of '{child.Name}' cannot be restored.");
                foreach (var row in rows)
                {
                    var rowId = await RecreateAsync(io, child, field, ResolveReferences(owned, row, recreated), path, owned, newId);
                    Remember(recreated, owned.Name, EditHistorySnapshot.GetId(row), rowId);
                }
            }
            return newId;
        }

        //従属レコードの宣言を、指される側 (References の値) が先に来る順に並べる
        static List<OwnedRecordsDesign> OrderByReferences(ModuleDesign design)
        {
            var rest = EditHistoryContracts.OwnedRecords(design).Select(e => e.Owned).ToList();
            var sorted = new List<OwnedRecordsDesign>();
            while (rest.Count != 0)
            {
                var ready = rest.Where(e => e.References.Values.All(target =>
                    target == e.Name || sorted.Any(s => s.Name == target) || rest.All(r => r.Name != target))).ToList();
                if (ready.Count == 0) ready = rest.ToList();
                sorted.AddRange(ready);
                rest.RemoveAll(ready.Contains);
            }
            return sorted;
        }

        //作り直した行 (旧 Id → 新 Id) を覚える。Id が変わらなかった行 (手入力 Id) は覚えない
        static void Remember(Dictionary<string, Dictionary<string, string>> recreated, string name, string oldId, string newId)
        {
            if (oldId.Length == 0 || oldId == newId) return;
            if (!recreated.TryGetValue(name, out var map)) recreated[name] = map = new Dictionary<string, string>();
            map[oldId] = newId;
        }

        //他の従属レコード群の行を指す項目を、指す先の行を作り直した Id に付け替えた複製を返す (付け替えが無ければそのまま)
        static ModuleData ResolveReferences(OwnedRecordsDesign owned, ModuleData row, Dictionary<string, Dictionary<string, string>> recreated)
        {
            ModuleData? copy = null;
            foreach (var (fieldName, targetName) in owned.References)
            {
                if (!recreated.TryGetValue(targetName, out var map)) continue;
                if (row.Fields.GetValueOrDefault(fieldName) is not ValueFieldDataBase<string> reference || string.IsNullOrEmpty(reference.Value)) continue;
                if (!map.TryGetValue(reference.Value, out var newId)) continue;
                copy ??= row.JsonClone();
                ((ValueFieldDataBase<string>)copy.Fields[fieldName]).Value = newId;
            }
            return copy ?? row;
        }

        //Id が変わったレコード (新しい Id での作り直し・Id を変えた更新) の、旧 Id の版を新しい Id に付け替える (そのモジュールが履歴を持つときだけ)
        internal async Task RelinkVersionsAsync(ModuleDataIOInternalAccess io, ModuleDesign design, string oldId, string newId)
        {
            var resolved = EditHistoryContracts.Resolve(designData, design, out _);
            if (resolved == null) return;
            var (historyModule, names) = resolved.Value;
            var versions = await io.GetListAsync(EditHistoryContracts.VersionsCondition(historyModule.Name, names, design.Name, oldId, null, SystemFieldNames.Id));
            foreach (var version in versions.Items)
            {
                var update = new ModuleData { Name = historyModule.Name };
                update.Fields[SystemFieldNames.Id] = version.Fields[SystemFieldNames.Id];
                if (historyModule.Fields.FirstOrDefault(e => e.Name == names.DataId)?.CreateData() is not TextFieldData dataId) return;
                dataId.Value = newId;
                update.Fields[names.DataId] = dataId;
                await io.UpdateAsync(update);
            }
        }

        //宣言の束縛 (子側のフィールド = SearchTargetVariable、親側のフィールド = Variable)。
        //子側が Id なら「親が子を参照する」宣言 (埋め込みモジュール)、それ以外は「子が親を参照する」宣言 (明細)
        sealed record Binding(string ChildField, string ParentField, bool ParentRefersChild);

        static Binding? GetBinding(OwnedRecordsDesign owned)
        {
            var bind = owned.Condition.GetFieldVariableConditions().FirstOrDefault();
            if (bind == null) return null;
            var childField = new VariableName(bind.SearchTargetVariable).FieldName.FullName;
            var parentField = new VariableName(bind.Variable).FieldName.FullName;
            return new Binding(childField, parentField, owned.IsReferencedByOwner);
        }

        //参照のフィールド (明細の親リンク・埋め込みモジュールの参照) に Id を入れる
        static void SetReference(ModuleDesign design, ModuleData data, string fieldName, string id)
        {
            var fieldData = design.Fields.FirstOrDefault(e => e.Name == fieldName)?.CreateData();
            switch (fieldData)
            {
                case ValueFieldDataBase<string> value: value.Value = id; break;
                case ModuleFieldData embedded: embedded.Id = id; break;
                default: return;
            }
            data.Fields[fieldName] = fieldData;
        }

        //Id が手入力 (または複合 Id) のモジュールか。Id がデータそのものなので、作り直しでも元の Id を保つ
        static bool IsManualId(ModuleDesign design)
            => design.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id) is { } id && (id.IsManualInput || id.CompositeIdVariables.Count != 0);
    }
}
