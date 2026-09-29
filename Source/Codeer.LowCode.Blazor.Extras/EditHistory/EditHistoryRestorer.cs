using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
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
    /// 対象外を宣言したフィールド (IEditHistoryExcludedFieldDesign。承認フローの参照など、サーバーだけが書く値) は反映しない。
    /// </summary>
    internal static class EditHistoryRestorer
    {
        /// <param name="onRevive">論理削除の行を Id を保って復活させるときの登録先 (モジュール名, Id)。null なら常に新しい行として追加する。</param>
        /// <returns>反映したフィールドの数 (値を入れたフィールド + 行を差し替えた従属レコード)。0 = この版から反映できるものが無かった。</returns>
        internal static async Task<int> ApplyAsync(Module module, ModuleData snapshot, Action<string, string>? onRevive)
        {
            var copy = snapshot.JsonClone();
            StripNotRestorable(module.Services.AppInfoService.GetDesignData(), copy);
            return await module.ApplyRecordAsync(copy, onRevive);
        }

        //添付ファイルと、対象外を宣言したフィールドの項目を外す (従属レコードの行の中まで)
        static void StripNotRestorable(DesignData designData, ModuleData data)
        {
            var excluded = designData.Modules.Find(data.Name)?.Fields.Where(e => e is IEditHistoryExcludedFieldDesign).Select(e => e.Name).ToHashSet() ?? [];
            foreach (var key in data.Fields.Where(e => e.Value is FileFieldData || excluded.Contains(e.Key)).Select(e => e.Key).ToList()) data.Fields.Remove(key);
            foreach (var fieldData in data.Fields.Values)
            {
                foreach (var row in (fieldData as IOwnedRecordsData)?.GetOwnedRows() ?? []) StripNotRestorable(designData, row);
            }
        }
    }
}
