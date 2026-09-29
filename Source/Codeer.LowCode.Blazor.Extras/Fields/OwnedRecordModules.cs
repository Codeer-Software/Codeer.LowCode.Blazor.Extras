using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Runtime.CompilerServices;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 子レコードを ModuleCollection で持つフィールド (Gantt / Calendar / TaskBoard / MarkerList) が、
    /// 本体の IOwnedRecordsField (従属レコードの差し替え・表示) を実装するための部品。
    /// </summary>
    internal static class OwnedRecordModules
    {
        //表示専用で見せている行 (CreateForShowAsync) の項目に付けるクラス。
        //行モジュールの ClassName はレイアウトのデザインで付けたクラスを含むので、項目 (バー・予定・カード・マーカー) にはこちらだけを出す
        static readonly ConditionalWeakTable<Module, string> _shownClassNames = new();

        /// <summary>表示専用で見せている行の項目に付けるクラス (OwnedRecordRow.ClassName)。通常の行は空。</summary>
        internal static string ShownClassName(this Module? module)
            => module != null && _shownClassNames.TryGetValue(module, out var className) ? className : string.Empty;

        /// <summary>
        /// 持っている行を与えられた行に差し替える。本体の ListField.ApplyOwnedRecordsAsync と同じ規則:
        /// 行 Id で突き合わせ、既存行は内容を反映、無い行は論理削除なら Id を保って復活 (onRevive に登録)、それ以外は新しい行、余った行は削除。
        /// </summary>
        /// <param name="layoutType">行のモジュールを作るレイアウト種別 (画面を持たない行は None)。</param>
        /// <returns>新しい行として追加した行の対応 (渡された行の Id → 作った行のモジュール)。Id を保った行 (既存・復活) は含まない。</returns>
        internal static async Task<Dictionary<string, Module>> ApplyAsync(FieldBase field, ModuleCollection collection, string moduleName, string layoutName,
            SearchCondition condition, IReadOnlyList<ModuleData> rows, Action<string, string>? onRevive, ModuleLayoutType layoutType = ModuleLayoutType.Detail)
        {
            var created = new Dictionary<string, Module>();
            var services = field.Services;
            var childDesign = services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            var canRevive = onRevive != null && childDesign != null && childDesign.UsesLogicalDelete();

            var existing = collection.Items.ToList();
            var byId = new Dictionary<string, Module>();
            foreach (var e in existing)
            {
                if (!e.IsNewData) byId.TryAdd(e.GetIdText(), e);
            }

            var kept = new HashSet<Module>();
            foreach (var child in rows)
            {
                var id = (child.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;
                if (id.Length != 0 && byId.TryGetValue(id, out var row))
                {
                    await row.ApplyRecordAsync(child, onRevive);
                    kept.Add(row);
                    continue;
                }

                Module mod;
                if (canRevive && id.Length != 0)
                {
                    //論理削除の行: Id を保った行を作り (既存行扱い)、保存時の削除の取り消しを登録する
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
                //行の中の従属レコード
                await mod.ApplyRecordAsync(child, onRevive);
            }

            foreach (var e in existing)
            {
                if (!kept.Contains(e)) collection.Remove(e);
            }
            return created;
        }

        /// <summary>与えられた行から表示専用のモジュールを作る (DB は読まない)。行ごとのクラスと行の中の従属レコードは OwnedRecordRow.ApplyToAsync。</summary>
        internal static async Task<List<Module>> CreateForShowAsync(FieldBase field, string layoutName, IReadOnlyList<OwnedRecordRow> rows,
            ModuleLayoutType layoutType = ModuleLayoutType.Detail)
        {
            var list = new List<Module>();
            foreach (var row in rows)
            {
                //行データごと作る (Id が実 Id なので新規扱いにならず「変更あり」にもならない。仮 Id で作って後から入れると
                //変更ありのまま残り、Gantt の週送りなどが「変更を破棄しますか」を出してしまう)
                var mod = await ModuleCreationService.CreateModuleAsync(field.Services, row.Data, layoutType, layoutName);
                mod.IsViewOnly = true;
                await row.ApplyToAsync(mod);
                if (!string.IsNullOrEmpty(row.ClassName)) _shownClassNames.AddOrUpdate(mod, row.ClassName);
                list.Add(mod);
            }
            return list;
        }

        //新しい行として入れるため Id・楽観ロック等のシステム値を外す
        static ModuleData StripForNewRow(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(OwnedRecordsExtensions.IsSystemOrLinkedField).ToList())
                copy.Fields.Remove(name);
            return copy;
        }

        //復活する行: Id は残し、楽観ロック等のシステム値と行の中の従属レコードは外す (従属レコードは ApplyRecordAsync で改めて反映する)
        static ModuleData StripForRevive(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(e => e != SystemFieldNames.Id
                && (OwnedRecordsExtensions.IsSystemOrLinkedField(e) || (copy.Fields[e] as IOwnedRecordsData)?.GetOwnedRows() != null)).ToList())
                copy.Fields.Remove(name);
            return copy;
        }
    }
}
