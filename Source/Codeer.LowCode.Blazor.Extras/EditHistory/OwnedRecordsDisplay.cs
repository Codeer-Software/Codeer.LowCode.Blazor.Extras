using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 版表示ダイアログ用。版の行と差分から表示用の行 (OwnedRecordRow) を組み立てる。
    /// 追加 = 行全体 / 変更 = 行に枠 + 変わったセル / 削除 = 前の版の行を元の位置に差し込んで打ち消し。孫の従属レコードも同じ規則で再帰。
    /// 行を見せる側 (本体の ListField / Gantt 等) は OwnedRecordRow.CreateModuleAsync で行のモジュールを作るだけ。
    /// </summary>
    internal static class OwnedRecordsDisplay
    {
        /// <summary>版の行と差分 (その従属レコードの EditHistoryChange。無ければ null) から表示用の行を作る。</summary>
        internal static List<OwnedRecordRow> Build(IReadOnlyList<ModuleData> rows, EditHistoryChange? change)
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
                if (row.Data.GetOwnedRows(change.FieldName) is { } nested)
                    row.OwnedRecords[change.FieldName] = Build(nested, change);
            }
        }

        //削除行: 行も、その中の従属レコードの行も全部打ち消し
        static OwnedRecordRow Removed(ModuleData data)
        {
            var row = new OwnedRecordRow { Data = data, ClassName = EditHistoryField.RemovedRowClassName };
            foreach (var name in data.Fields.Keys)
            {
                if (data.GetOwnedRows(name) is { } nested) row.OwnedRecords[name] = nested.Select(Removed).ToList();
            }
            return row;
        }
    }
}
