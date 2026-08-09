using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// Resolves mail template variables from module data as display strings:
    /// Select/Link fields use their display text, formatted fields (number/date/time) use the
    /// design's external text format, anything else falls back to the raw value.
    /// Missing fields and null values resolve to an empty string.
    /// </summary>
    internal static class MailVariableResolver
    {
        public static Dictionary<string, string> Resolve(ModuleDesign? design, ModuleData data, IEnumerable<string> names)
        {
            var variables = new Dictionary<string, string>();
            foreach (var name in names)
            {
                variables[name] = ResolveOne(design, data, name);
            }
            return variables;
        }

        public static string ResolveOne(ModuleDesign? design, ModuleData data, string name)
        {
            if (!data.Fields.TryGetValue(name, out var fieldData) || fieldData == null) return string.Empty;

            //表示テキストを持つ型はそれを優先(コード値ではなく人が読む文字列)
            if (fieldData is SelectFieldData select)
                return !string.IsNullOrEmpty(select.DisplayText) ? select.DisplayText : select.Value ?? string.Empty;
            if (fieldData is LinkFieldData link)
                return !string.IsNullOrEmpty(link.DisplayText) ? link.DisplayText : link.Value ?? string.Empty;

            var value = fieldData.GetType().GetProperty("Value")?.GetValue(fieldData);
            if (value == null) return string.Empty;

            //数値・日付等はデザインの外部テキスト書式(一括入出力と同じ見え方)で整形する
            var fieldDesign = design?.Fields.FirstOrDefault(e => e.Name == name);
            if (fieldDesign is IExternalTextFormatFieldDesign format) return format.FormatExternalText(value);

            return value.ToString() ?? string.Empty;
        }

        /// <summary>Reads a field value as a plain string (for address fields).</summary>
        public static string GetValueText(ModuleData data, string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            if (!data.Fields.TryGetValue(name, out var fieldData) || fieldData == null) return string.Empty;
            return fieldData.GetType().GetProperty("Value")?.GetValue(fieldData)?.ToString() ?? string.Empty;
        }

        /// <summary>Reads a boolean field value (for the opt-out exclude flag).</summary>
        public static bool GetBooleanValue(ModuleData data, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!data.Fields.TryGetValue(name, out var fieldData)) return false;
            return (fieldData as BooleanFieldData)?.Value == true;
        }
    }
}
