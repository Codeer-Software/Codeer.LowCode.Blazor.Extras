using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 過去の版のスナップショットを編集中のモジュールへ反映する (保存はしない = ユーザーが Submit で確定)。
    /// 反映の本体は本体の Module.ApplyRecordAsync (値は SetDataAsync で変更扱い、従属レコードは IOwnedRecordsField で行 Id の突き合わせ・
    /// 論理削除の行の Id を保った復活 (onRevive)・余った行の削除。システムフィールド・リンク越し・従属でない一覧・書き込み権限のないフィールドは対象外)。
    /// 履歴側の規則は 2 つ: 添付ファイルは履歴に実体を持たないので反映しない (名前・キーも戻さない)。
    /// 承認フローの FK (ApprovalFlowFieldData) はサーバーだけが書くので反映しない (反映すると画面の承認状態だけが過去の版に戻る)。
    /// </summary>
    internal static class EditHistoryRestorer
    {
        /// <param name="onRevive">論理削除の行を Id を保って復活させるときの登録先 (モジュール名, Id)。null なら常に新しい行として追加する。</param>
        /// <returns>反映したフィールドの数 (値を入れたフィールド + 行を差し替えた従属レコード)。0 = この版から反映できるものが無かった。</returns>
        internal static async Task<int> ApplyAsync(Module module, ModuleData snapshot, Action<string, string>? onRevive)
        {
            var data = StripNotRestorable(snapshot);
            var applied = await module.ApplyRecordAsync(data, onRevive);
            //本体の ApplyRecordAsync はフィールド名と同じ名前の宣言だけを差し替える。フィールドが自分の名前以外で宣言した従属レコード
            //(Gantt の依存関係 "Gantt:Dependencies" 等) はここで差し替える (対象と条件は本体と同じ: 書き込み権限のあるフィールドで、版にある宣言だけ)
            foreach (var field in module.GetFields())
            {
                if (field is not IOwnedRecordsField owned || field.Design is not IOwnedRecordsFieldDesign design || !field.HasUserWritePermission) continue;
                foreach (var declared in design.GetOwnedRecords())
                {
                    if (declared.Name == field.Design.Name) continue;
                    if (data.Fields.GetValueOrDefault(declared.Name) is not ListFieldData rows) continue;
                    await owned.ApplyOwnedRecordsAsync(declared.Name, rows.Children, onRevive);
                    applied++;
                }
            }
            return applied;
        }

        //添付ファイル・承認フローの FK の項目を外す (孫・埋め込みの中まで)
        static ModuleData StripNotRestorable(ModuleData src)
        {
            var copy = src.JsonClone();
            StripCore(copy);
            return copy;
        }

        static void StripCore(ModuleData data)
        {
            foreach (var key in data.Fields.Where(e => e.Value is FileFieldData or ApprovalFlowFieldData).Select(e => e.Key).ToList()) data.Fields.Remove(key);
            foreach (var fieldData in data.Fields.Values)
            {
                switch (fieldData)
                {
                    case ListFieldData list: list.Children.ForEach(StripCore); break;
                    case ModuleFieldData module: StripCore(module.Data); break;
                }
            }
        }
    }
}
