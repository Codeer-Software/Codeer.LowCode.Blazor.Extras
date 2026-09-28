using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 版表示ダイアログ用。版の行と差分から表示用の行 (OwnedRecordRow) を組み立てる。
    /// 追加 = 行全体 / 変更 = 行に枠 + 変わったセル / 削除 = 前の版の行を元の位置に差し込んで打ち消し。孫の従属レコードも同じ規則で再帰。
    /// 行を見せる側 (本体の ListField / Gantt 等) は OwnedRecordRow.ApplyToAsync でクラスを写すだけ。
    /// </summary>
    internal static class OwnedRecordsDisplay
    {
        /// <summary>拡張フィールド用: 版の行から表示専用のモジュールを作る (DB は読まない)。行ごとのクラスと孫は ApplyToAsync。</summary>
        internal static async Task<List<Module>> CreateAsync(FieldBase field, string moduleName, string layoutName, IReadOnlyList<OwnedRecordRow> rows,
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
                list.Add(mod);
            }
            return list;
        }

        /// <summary>版の行と差分 (その従属レコードの EditHistoryChange。無ければ null) から表示用の行を作る。</summary>
        internal static List<OwnedRecordRow> Build(List<ModuleData> rows, EditHistoryChange? change)
        {
            var changesById = new Dictionary<string, EditHistoryRowChange>();
            foreach (var rowChange in change?.Rows ?? new())
            {
                if (rowChange.Row == null || rowChange.Kind == EditHistoryRowChangeKind.Removed) continue;
                var id = EditHistorySnapshot.GetId(rowChange.Row);
                if (id.Length != 0) changesById.TryAdd(id, rowChange);
            }

            var result = new List<OwnedRecordRow>();
            var ids = new HashSet<string>();
            foreach (var data in rows)
            {
                var row = new OwnedRecordRow { Data = data };
                var id = EditHistorySnapshot.GetId(data);
                if (id.Length != 0)
                {
                    ids.Add(id);
                    if (changesById.TryGetValue(id, out var rowChange)) Decorate(row, rowChange);
                }
                result.Add(row);
            }

            //削除行: 前の版の位置に差し込む (表示専用。Id はそのまま = 保存には載らない)
            if (change != null)
            {
                foreach (var rowChange in change.Rows.Where(e => e.Kind == EditHistoryRowChangeKind.Removed && e.Row != null).OrderBy(e => e.RowNumber))
                {
                    var id = EditHistorySnapshot.GetId(rowChange.Row!);
                    if (id.Length != 0 && !ids.Add(id)) continue;
                    result.Insert(Math.Min(Math.Max(rowChange.RowNumber - 1, 0), result.Count), Removed(rowChange.Row!));
                }
            }
            return result;
        }

        //追加行 = 行全体。変更行 = 行に枠 + 変わったセル。行の中の従属レコード (孫) の差分は同じ規則で再帰
        static void Decorate(OwnedRecordRow row, EditHistoryRowChange rowChange)
        {
            if (rowChange.Kind == EditHistoryRowChangeKind.Added)
            {
                row.ClassName = EditHistoryField.AddedRowClassName;
                return;
            }
            row.ClassName = EditHistoryField.ChangedRowClassName;
            foreach (var change in rowChange.Changes)
            {
                if (!change.IsList)
                {
                    row.FieldClassNames[change.FieldName] = EditHistoryField.ChangedClassName;
                    continue;
                }
                if (row.Data.Fields.GetValueOrDefault(change.FieldName) is ListFieldData nested)
                    row.OwnedRecords[change.FieldName] = Build(nested.Children, change);
            }
        }

        //削除行: 行も、その中の従属レコードの行も全部打ち消し
        static OwnedRecordRow Removed(ModuleData data)
        {
            var row = new OwnedRecordRow { Data = data, ClassName = EditHistoryField.RemovedRowClassName };
            foreach (var (name, fieldData) in data.Fields)
            {
                if (fieldData is ListFieldData nested) row.OwnedRecords[name] = nested.Children.Select(Removed).ToList();
            }
            return row;
        }
    }
}
