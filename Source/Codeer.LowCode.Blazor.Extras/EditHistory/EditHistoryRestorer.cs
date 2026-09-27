using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 過去の版のスナップショットを編集中のモジュールへ反映する (保存はしない = ユーザーが Submit で確定)。
    /// 反映の本体は本体の Module.ApplyRecordAsync (値は SetDataAsync で変更扱い、従属レコードは IOwnedRecordsField で行 Id の突き合わせ・
    /// 論理削除の行の Id を保った復活 (onRevive)・余った行の削除。システムフィールド・リンク越し・従属でない一覧・書き込み権限のないフィールドは対象外)。
    /// 履歴側の規則はひとつだけ: 添付ファイルは履歴に実体を持たないので反映しない (名前・キーも戻さない)。
    /// </summary>
    internal static class EditHistoryRestorer
    {
        /// <param name="onRevive">論理削除の行を Id を保って復活させるときの登録先 (モジュール名, Id)。null なら常に新しい行として追加する。</param>
        /// <returns>反映したフィールドの数 (値を入れたフィールド + 行を差し替えた従属レコード)。0 = この版から反映できるものが無かった。</returns>
        internal static async Task<int> ApplyAsync(Module module, ModuleData snapshot, Action<string, string>? onRevive)
            => await module.ApplyRecordAsync(StripFiles(snapshot), onRevive);

        //添付ファイルの項目を外す (孫まで)
        static ModuleData StripFiles(ModuleData src)
        {
            var copy = src.JsonClone();
            StripFilesCore(copy);
            return copy;
        }

        static void StripFilesCore(ModuleData data)
        {
            foreach (var key in data.Fields.Where(e => e.Value is FileFieldData).Select(e => e.Key).ToList()) data.Fields.Remove(key);
            foreach (var list in data.Fields.Values.OfType<ListFieldData>()) list.Children.ForEach(StripFilesCore);
        }
    }
}
