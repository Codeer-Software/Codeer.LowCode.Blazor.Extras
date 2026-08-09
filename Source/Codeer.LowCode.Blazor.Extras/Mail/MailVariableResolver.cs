using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// Resolves mail template variables from module data.
    /// Tokens follow the design variable notation: a field path (link paths like "Contact.Email" allowed
    /// because list data holds them as "Contact.Email" keys) plus an optional trailing member.
    /// - No member / ".DisplayText": display string (Select/Link use their display text, formatted fields
    ///   use the design's external text format).
    /// - ".Value": the value itself (Select/Link give the code value; formatted fields still use the
    ///   external text format because mail is a plain-text medium with no cell format).
    /// Missing fields and null values resolve to an empty string.
    /// </summary>
    internal static class MailVariableResolver
    {
        static readonly string[] KnownMembers = { "Value", "DisplayText" };

        /// <summary>
        /// Splits a token into the field path (= ModuleData.Fields key) and the member.
        /// The last segment is a member only when it is a known member name, so both
        /// "Contact.Email" (field path) and "Contact.Email.Value" (path + member) parse correctly.
        /// </summary>
        public static (string FieldPath, string Member) ParseToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return (string.Empty, string.Empty);
            var variable = new VariableName(token);
            if (KnownMembers.Contains(variable.MemberName)) return (variable.FieldName.FullName, variable.MemberName);
            return (token, string.Empty);
        }

        public static Dictionary<string, string> Resolve(ModuleDesign? design, ModuleData data, IEnumerable<string> names,
            Func<string, ModuleDesign?>? findModule = null)
        {
            var variables = new Dictionary<string, string>();
            foreach (var name in names)
            {
                variables[name] = ResolveOne(design, data, name, findModule);
            }
            return variables;
        }

        public static string ResolveOne(ModuleDesign? design, ModuleData data, string name,
            Func<string, ModuleDesign?>? findModule = null)
        {
            var (fieldPath, member) = ParseToken(name);
            if (string.IsNullOrEmpty(fieldPath)) return string.Empty;
            if (!data.Fields.TryGetValue(fieldPath, out var fieldData) || fieldData == null) return string.Empty;

            //表示テキストを持つ型はそれを優先(コード値ではなく人が読む文字列)。".Value"明示時はコード値を返す
            if (member != "Value")
            {
                if (fieldData is SelectFieldData select)
                    return !string.IsNullOrEmpty(select.DisplayText) ? select.DisplayText : select.Value ?? string.Empty;
                if (fieldData is LinkFieldData link)
                    return !string.IsNullOrEmpty(link.DisplayText) ? link.DisplayText : link.Value ?? string.Empty;
                if (member == "DisplayText") return string.Empty; //表示テキストを持たない型
            }

            var value = fieldData.GetType().GetProperty("Value")?.GetValue(fieldData);
            if (value == null) return string.Empty;

            //数値・日付等はデザインの外部テキスト書式(一括入出力と同じ見え方)で整形する
            if (FindFieldDesign(design, fieldPath, findModule) is IExternalTextFormatFieldDesign format)
                return format.FormatExternalText(value);

            return value.ToString() ?? string.Empty;
        }

        /// <summary>Reads a field value as a plain string (for address fields). Accepts field paths and a trailing ".Value".</summary>
        public static string GetValueText(ModuleData data, string name)
        {
            var (fieldPath, _) = ParseToken(name);
            if (string.IsNullOrEmpty(fieldPath)) return string.Empty;
            if (!data.Fields.TryGetValue(fieldPath, out var fieldData) || fieldData == null) return string.Empty;
            return fieldData.GetType().GetProperty("Value")?.GetValue(fieldData)?.ToString() ?? string.Empty;
        }

        /// <summary>Reads a boolean field value (for the opt-out exclude flag). Accepts field paths and a trailing ".Value".</summary>
        public static bool GetBooleanValue(ModuleData data, string name)
        {
            var (fieldPath, _) = ParseToken(name);
            if (string.IsNullOrEmpty(fieldPath)) return false;
            if (!data.Fields.TryGetValue(fieldPath, out var fieldData)) return false;
            return (fieldData as BooleanFieldData)?.Value == true;
        }

        //リンクパスはLink/SelectFieldの参照先モジュールを辿ってフィールドデザインを引く(書式整形用)。
        //findModule が無い環境では辿れないぶん整形なし(ToString)になるだけで解決自体は成立する
        static FieldDesignBase? FindFieldDesign(ModuleDesign? design, string fieldPath,
            Func<string, ModuleDesign?>? findModule)
        {
            if (design == null) return null;
            var segments = fieldPath.Split('.');
            var current = design;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var field = current.Fields.FirstOrDefault(e => e.Name == segments[i]);
                var linkModuleName = field switch
                {
                    LinkFieldDesign linkField => linkField.SearchCondition.ModuleName,
                    SelectFieldDesign selectField => selectField.SearchCondition.ModuleName,
                    _ => string.Empty,
                };
                if (string.IsNullOrEmpty(linkModuleName) || findModule == null) return null;
                current = findModule(linkModuleName);
                if (current == null) return null;
            }
            return current.Fields.FirstOrDefault(e => e.Name == segments[^1]);
        }
    }
}
