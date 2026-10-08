using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

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

        /// <summary>
        /// DB で NULL だった列を null 値のデータとして埋める (孫まで)。本体の読み出しは NULL の列を ModuleData に入れないため、
        /// そのままだと「その版では空だった」ことが版に残らず、復元でその項目を空に戻せない (無い項目は触らないため)。
        /// 記録は内部読み (権限で列が落ちない) なので、無い列 = NULL とみなしてよい。対象は DB 列を持つ値フィールド。システムフィールドは埋めない。
        /// </summary>
        internal static ModuleData? FillNulls(DesignData designData, ModuleData? data)
        {
            if (data == null) return null;
            var design = designData.Modules.Find(data.Name);
            if (design == null) return data;
            foreach (var field in design.Fields)
            {
                if (field is not DbValueFieldDesignBase || EditHistoryContracts.IsExcludedField(field.Name)) continue;
                if (data.Fields.ContainsKey(field.Name)) continue;
                var empty = field.CreateData();
                if (empty != null) data.Fields[field.Name] = empty;
            }
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(designData.Modules, design))
            {
                foreach (var row in data.GetOwnedRows(owned.Name) ?? []) FillNulls(designData, row);
            }
            return data;
        }

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
