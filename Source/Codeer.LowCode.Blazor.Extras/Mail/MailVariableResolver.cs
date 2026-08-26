using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// メールテンプレート変数をモジュールデータから解決する。
    /// トークンはデザインの変数表記: フィールドパス (リスト取得データが "Contact.Email" キーで持つため
    /// リンクパス可) + 省略可能な末尾メンバー。
    /// - メンバー省略 / ".DisplayText": 表示文字列 (Select/Link は表示テキスト、書式付きフィールドは
    ///   デザインの外部テキスト書式)。
    /// - ".Value": 値そのもの (Select/Link はコード値。書式付きフィールドは外部テキスト書式のまま =
    ///   メールはセル書式の無いプレーンテキスト媒体のため)。
    /// フィールド不在・null 値は空文字になる。
    /// </summary>
    internal static class MailVariableResolver
    {
        static readonly string[] KnownMembers = { "Value", "DisplayText" };

        /// <summary>
        /// トークンをフィールドパス (= ModuleData.Fields のキー) とメンバーに分解する。
        /// 末尾セグメントは既知のメンバー名のときだけメンバー扱いにするため、
        /// "Contact.Email" (パスのみ) も "Contact.Email.Value" (パス+メンバー) も正しく読める。
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
