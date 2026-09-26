using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 過去の版のスナップショットを編集中のモジュールへ反映する (保存はしない = ユーザーが Submit で確定)。
    /// 値フィールドは SetDataAsync (変更扱い・OnDataChanged スクリプトも動く)。
    /// 従属レコード (IOwnedRecordsFieldDesign の宣言) は行 Id で突き合わせ、既存行は更新、余った行は削除、無い行は
    /// - 明細モジュールが論理削除なら Id を保ったまま行を復活 (保存時に Undelete を送る = onRevive で登録)
    /// - それ以外は新しい行として追加 (Id は振り直し)
    /// 対象外: システムフィールド (Id / 楽観ロック / 作成・更新記録 / 論理削除)、リンク越しの派生値、
    /// 従属でない一覧、添付ファイル (実体を履歴に持たないため)、書き込み権限のないフィールド。
    /// </summary>
    internal static class EditHistoryRestorer
    {
        /// <param name="onRevive">論理削除の行を Id を保って復活させるときの登録先 (モジュール名, Id)。null なら常に新しい行として追加する。</param>
        internal static async Task ApplyAsync(Module module, ModuleData snapshot, Action<string, string>? onRevive)
        {
            foreach (var field in module.GetFields())
            {
                var name = field.Design.Name;
                if (EditHistoryContracts.IsExcludedField(name)) continue;
                if (!field.HasUserWritePermission) continue;
                if (!snapshot.Fields.TryGetValue(name, out var data)) continue;

                switch (field)
                {
                    case ListField list:
                        if (list.Design.GetOwnedRecords().Any() && data is ListFieldData listData)
                            await ApplyRowsAsync(list, listData, onRevive);
                        break;
                    case IOwnedRecordsField owned:
                        //別モジュールのレコードを自分で持つ拡張フィールド (Gantt 等): 宣言した従属レコードを版の内容に差し替える
                        if (data is ListFieldData ownedData)
                            await owned.ApplyOwnedRecordsAsync(name, ownedData.Children, onRevive);
                        break;
                    case FileField:
                        break;
                    default:
                        if (data is ListFieldData) break;
                        await field.SetDataAsync(data);
                        break;
                }
            }
        }

        static async Task ApplyRowsAsync(ListField list, ListFieldData data, Action<string, string>? onRevive)
        {
            //レイアウトに置いていない一覧 (Gantt 等の拡張フィールドの子レコードを履歴に載せるためだけの一覧) は
            //行を読み込んでいないので、突き合わせの前に読み込む
            if (!list.IsInLayout() && list.RowCount == 0)
            {
                list.AllowLoad = true;
                await list.ReloadAsync();
            }
            var rows = list.Rows;
            var rowsById = new Dictionary<string, Module>();
            foreach (var row in rows)
            {
                if (row.IsNewData) continue;
                rowsById.TryAdd(row.GetIdText(), row);
            }

            var childDesign = list.Services.AppInfoService.GetDesignData().Modules.Find(list.ModuleName);
            var canRevive = onRevive != null && childDesign != null && EditHistoryContracts.IsLogicalDeleteModule(childDesign);

            var kept = new HashSet<Module>();
            foreach (var child in data.Children)
            {
                var id = EditHistorySnapshot.GetId(child);
                if (id.Length != 0 && rowsById.TryGetValue(id, out var row))
                {
                    await ApplyAsync(row, child, onRevive);
                    kept.Add(row);
                    continue;
                }

                if (canRevive && id.Length != 0)
                {
                    //論理削除の行: Id を保った行を作り (InsertRowsAsync は Id 付きなら既存行として扱う)、保存時の Undelete を登録する。
                    //孫の明細は行を作ってから同じ手順で反映する
                    var added = await list.InsertRowsAsync(list.RowCount, [StripForRevive(child)]);
                    onRevive!(childDesign!.Name, id);
                    foreach (var e in added)
                    {
                        kept.Add(e);
                        await ApplyAsync(e, child, onRevive);
                    }
                    continue;
                }

                //無い行 (物理削除済み) は新しい行として追加する (Id は振り直し = 既知の割り切り)
                var inserted = await list.InsertRowsAsync(list.RowCount, [StripSystemFields(child)]);
                foreach (var e in inserted) kept.Add(e);
            }

            foreach (var row in rows)
            {
                if (!kept.Contains(row)) await list.DeleteRowAsync(row);
            }
        }

        //新規行として入れるため Id・楽観ロック等のシステム値を外す (孫の明細は ListField 側が同じ処理をする)
        internal static ModuleData StripSystemFields(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(e => EditHistoryContracts.IsExcludedField(e)).ToList())
                copy.Fields.Remove(name);
            return copy;
        }

        //復活する行: Id は残し、楽観ロック等のシステム値と孫の一覧は外す (孫は ApplyAsync で改めて反映する)
        internal static ModuleData StripForRevive(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(e => e != SystemFieldNames.Id && (EditHistoryContracts.IsExcludedField(e) || copy.Fields[e] is ListFieldData)).ToList())
                copy.Fields.Remove(name);
            return copy;
        }
    }
}
