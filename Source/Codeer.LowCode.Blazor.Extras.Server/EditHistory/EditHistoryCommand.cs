using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Server.EditHistory
{
    /// <summary>
    /// 履歴行の Command (受け取った操作) = サーバーに送られてきた ModuleSubmitData の JSON。
    /// Snapshot が「保存の結果」なのに対し、こちらは「何が送られてきたか」(Add / Update / Delete / SearchDelete / ExtendedData / 編集中データ) を監査用にそのまま残す。
    /// 復元には使わない (差分はスナップショットから出す) ので、形は本体の型にそのまま追従する。
    /// 落とすのは 2 つだけ: パスワードの平文 (PasswordFieldData の値) と添付ファイルの中身 (FileFieldData.Content)。仮 Id は保存で採番された実 Id に置き換える。
    /// </summary>
    internal static class EditHistoryCommand
    {
        internal static string Serialize(ModuleSubmitData submitData, IReadOnlyDictionary<string, string>? temporaryIdMap)
        {
            var copy = submitData.JsonClone();
            Strip(copy.CurrentEditingData);
            foreach (var data in copy.Add) Strip(data);
            foreach (var data in copy.Update) Strip(data);
            var json = JsonConverterEx.SerializeObject(copy, false);
            if (temporaryIdMap != null)
            {
                //仮 Id はレコード・明細の Id と親へのリンクに同じ文字列で現れるので、文字列置換で全部を実 Id にする
                foreach (var (temporaryId, realId) in temporaryIdMap)
                {
                    if (!string.IsNullOrEmpty(temporaryId) && !string.IsNullOrEmpty(realId)) json = json.Replace(temporaryId, realId);
                }
            }
            return json;
        }

        static void Strip(ModuleData? data)
        {
            if (data == null) return;
            foreach (var fieldData in data.Fields.Values)
            {
                switch (fieldData)
                {
                    case PasswordFieldData password:
                        password.Value = null;
                        break;
                    case FileFieldData file:
                        file.Content = null;
                        break;
                    case ListFieldData list:
                        list.Children.ForEach(Strip);
                        break;
                    case ModuleFieldData module:
                        Strip(module.Data);
                        break;
                }
            }
        }
    }
}
