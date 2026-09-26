using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 履歴行に保存するスナップショット (ModuleData の JSON)。
    /// サーバー (記録) とクライアント (差分表示・復元) が同じ形を使う。
    /// 添付ファイルは名前とキーだけ (Content は落とす = 実体は履歴に残さない)。
    /// </summary>
    internal static class EditHistorySnapshot
    {
        internal static string Serialize(ModuleData data)
            => JsonConverterEx.SerializeObject(Strip(data), false);

        internal static ModuleData? Deserialize(string? json)
            => string.IsNullOrEmpty(json) ? null : JsonConverterEx.DeserializeObject<ModuleData>(json);

        internal static string GetId(ModuleData data)
            => (data.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;

        //ファイルの中身は持たない (履歴が肥大化する・復元で実体は戻さない仕様)
        static ModuleData Strip(ModuleData src)
        {
            var copy = src.JsonClone();
            StripCore(copy);
            return copy;
        }

        static void StripCore(ModuleData data)
        {
            foreach (var fieldData in data.Fields.Values)
            {
                switch (fieldData)
                {
                    case FileFieldData file:
                        file.Content = null;
                        break;
                    case ListFieldData list:
                        list.Children.ForEach(StripCore);
                        break;
                    case ModuleFieldData module:
                        StripCore(module.Data);
                        break;
                }
            }
        }
    }
}
