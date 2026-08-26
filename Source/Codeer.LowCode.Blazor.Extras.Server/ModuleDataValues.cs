using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Server
{
    /// <summary>
    /// ModuleData のフィールド値を契約の役割名 (フィールド名) で読む共通ヘルパ。
    /// 役割が空・フィールド不在・型違いは既定値 (空文字 / 0 / false)。
    /// </summary>
    internal static class ModuleDataValues
    {
        internal static string GetId(ModuleData data)
            => (data.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;

        internal static string GetString(ModuleData? data, string fieldName)
            => string.IsNullOrEmpty(fieldName)
                ? string.Empty
                : (data?.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value ?? string.Empty;

        internal static int GetInt(ModuleData data, string fieldName)
            => (int)((data.Fields.GetValueOrDefault(fieldName) as NumberFieldData)?.Value ?? 0);

        internal static bool GetBool(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as BooleanFieldData)?.Value == true;

        /// <summary>Optional role: empty role name or empty value = default.</summary>
        internal static string GetStringOrDefault(ModuleData data, string fieldName, string defaultValue)
        {
            if (string.IsNullOrEmpty(fieldName)) return defaultValue;
            var value = GetString(data, fieldName);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }

        /// <summary>Optional role: empty role name or null value = default.</summary>
        internal static bool GetBoolOrDefault(ModuleData data, string fieldName, bool defaultValue)
        {
            if (string.IsNullOrEmpty(fieldName)) return defaultValue;
            return (data.Fields.GetValueOrDefault(fieldName) as BooleanFieldData)?.Value ?? defaultValue;
        }
    }
}
