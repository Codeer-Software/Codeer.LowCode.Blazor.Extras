using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// ModuleCollection (Gantt / Calendar / TaskBoard / MarkerList が子レコードを保持する入れ物) を版の行に差し替える。
    /// 本体の ListField.ApplyOwnedRecordsAsync と同じ規則: 行 Id で突き合わせ、既存行は更新、
    /// 無い行は論理削除なら Id を保って復活 (保存時の Undelete)、それ以外は新しい行、余った行は削除。
    /// </summary>
    internal static class OwnedRecordsRestore
    {
        /// <param name="layoutType">行のモジュールを作るレイアウト種別 (一覧・Gantt のタスク等は Detail、Gantt の依存関係のように画面を持たない行は None)。</param>
        /// <returns>新しい行として追加した行の対応 (版の行の Id → 作った行のモジュール)。Id を保った行 (既存・復活) は含まない。</returns>
        internal static async Task<Dictionary<string, Module>> ApplyAsync(FieldBase field, ModuleCollection collection, string moduleName, string layoutName,
            SearchCondition condition, IReadOnlyList<ModuleData> rows, Action<string, string>? onRevive, ModuleLayoutType layoutType = ModuleLayoutType.Detail)
        {
            var created = new Dictionary<string, Module>();
            var services = field.Services;
            var childDesign = services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            var canRevive = onRevive != null && childDesign != null && EditHistoryContracts.IsLogicalDeleteModule(childDesign);

            var existing = collection.Items.ToList();
            var byId = new Dictionary<string, Module>();
            foreach (var e in existing)
            {
                if (!e.IsNewData) byId.TryAdd(e.GetIdText(), e);
            }

            var kept = new HashSet<Module>();
            foreach (var child in rows)
            {
                var id = EditHistorySnapshot.GetId(child);
                if (id.Length != 0 && byId.TryGetValue(id, out var row))
                {
                    await EditHistoryRestorer.ApplyAsync(row, child, onRevive);
                    kept.Add(row);
                    continue;
                }

                Module mod;
                if (canRevive && id.Length != 0)
                {
                    //論理削除の行: Id を保った行を作り (既存行扱い)、保存時の Undelete を登録する
                    var idOnly = new ModuleData { Name = moduleName };
                    idOnly.Fields[SystemFieldNames.Id] = child.Fields[SystemFieldNames.Id];
                    mod = await ModuleCreationService.CreateModuleAsync(services, idOnly, layoutType, layoutName);
                    await mod.SetDataWithoutInteractionAsync(StripForRevive(child));
                    onRevive!(childDesign!.Name, id);
                }
                else
                {
                    mod = await field.CreateChildModuleAsync(moduleName, layoutType, layoutName);
                    await field.AssignConditionValuesAsync(condition, mod);
                    await mod.SetDataWithoutInteractionAsync(StripForNewRow(child));
                    if (id.Length != 0) created[id] = mod;
                }
                collection.Add(mod);
                kept.Add(mod);
                //孫 (行の中の従属レコード)
                await EditHistoryRestorer.ApplyAsync(mod, child, onRevive);
            }

            foreach (var e in existing)
            {
                if (!kept.Contains(e)) collection.Remove(e);
            }
            return created;
        }

        //新しい行として入れるため Id・楽観ロック等のシステム値を外す
        static ModuleData StripForNewRow(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(e => EditHistoryContracts.IsExcludedField(e)).ToList())
                copy.Fields.Remove(name);
            return copy;
        }

        //復活する行: Id は残し、楽観ロック等のシステム値と孫の一覧は外す (孫は ApplyAsync で改めて反映する)
        static ModuleData StripForRevive(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(e => e != SystemFieldNames.Id && (EditHistoryContracts.IsExcludedField(e) || copy.Fields[e] is ListFieldData)).ToList())
                copy.Fields.Remove(name);
            return copy;
        }
    }
}
