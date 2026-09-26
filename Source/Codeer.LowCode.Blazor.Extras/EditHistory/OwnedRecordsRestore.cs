using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// ModuleCollection (Gantt / Calendar / TaskBoard が子レコードを保持する入れ物) を版の行に差し替える。
    /// ListField の復元 (EditHistoryRestorer.ApplyRowsAsync) と同じ規則: 行 Id で突き合わせ、既存行は更新、
    /// 無い行は論理削除なら Id を保って復活 (保存時の Undelete)、それ以外は新しい行、余った行は削除。
    /// </summary>
    internal static class OwnedRecordsRestore
    {
        internal static async Task ApplyAsync(FieldBase field, ModuleCollection collection, string moduleName, string layoutName,
            SearchCondition condition, List<ModuleData> rows, Action<string, string>? onRevive)
        {
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
                    mod = await ModuleCreationService.CreateModuleAsync(services, idOnly, ModuleLayoutType.Detail, layoutName);
                    await mod.SetDataWithoutInteractionAsync(EditHistoryRestorer.StripForRevive(child));
                    onRevive!(childDesign!.Name, id);
                }
                else
                {
                    mod = await field.CreateChildModuleAsync(moduleName, ModuleLayoutType.Detail, layoutName);
                    await field.AssignConditionValuesAsync(condition, mod);
                    await mod.SetDataWithoutInteractionAsync(EditHistoryRestorer.StripSystemFields(child));
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
        }
    }
}
