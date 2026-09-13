using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField が索引にする文章を、モジュールの 1 行 (<see cref="ModuleData"/>) から組み立てる。
    /// 形は「表示名: 値」を 1 行ずつ (値が空のフィールドは出さない)。候補値は表示名、リンクは表示文字列、日付は ISO 形式。
    /// クライアント (Submit 時) とサーバー (再索引・一括取込) の両方が同じ規則で作るためにここに置く。
    /// </summary>
    public static class SemanticSearchText
    {
        static readonly Regex _tags = new("<[^>]+>", RegexOptions.Compiled);

        /// <summary>行を文章にする。対象フィールドは <see cref="SemanticSearchFieldDesign.SourceFields"/> (空なら <see cref="DefaultSourceFields"/>)。</summary>
        /// <param name="design">候補値 (Enum) の解決に使う。null なら SelectField は値のまま</param>
        public static string Build(DesignData? design, ModuleDesign module, ModuleData row, SemanticSearchFieldDesign field)
        {
            var sb = new StringBuilder();
            foreach (var name in SourceFields(module, field))
            {
                var fieldDesign = module.Fields.FirstOrDefault(f => f.Name == name);
                if (fieldDesign == null || !row.Fields.TryGetValue(name, out var data) || data == null) continue;
                var value = FormatValue(design, fieldDesign, data);
                if (string.IsNullOrWhiteSpace(value)) continue;
                var label = (fieldDesign as ValueFieldDesignBase)?.DisplayName;
                //改行は環境によらず LF (索引の文章はどの OS で作っても同じにする)
                sb.Append(string.IsNullOrEmpty(label) ? name : label).Append(": ").Append(value.Trim()).Append('\n');
            }
            var text = sb.ToString().TrimEnd();
            return field.MaxTextLength > 0 && text.Length > field.MaxTextLength ? text[..field.MaxTextLength] : text;
        }

        /// <summary>文章にするフィールド名 (デザインの指定、空なら既定)。</summary>
        public static IEnumerable<string> SourceFields(ModuleDesign module, SemanticSearchFieldDesign field)
            => field.SourceFields.Count > 0 ? field.SourceFields : DefaultSourceFields(module);

        /// <summary>
        /// 既定の対象 = DB 列を持つ入力フィールド全部。Id・論理削除・楽観ロック・作成/更新の記録・パスワード・SemanticSearchField 自身は除く。
        /// </summary>
        public static IEnumerable<string> DefaultSourceFields(ModuleDesign module)
        {
            foreach (var f in module.Fields)
            {
                if (f is not DbValueFieldDesignBase db || string.IsNullOrEmpty(db.DbColumn)) continue;
                if (f is IdFieldDesign || f is PasswordFieldDesign || f is OptimisticLockingFieldDesign) continue;
                if (f.Name is SystemFieldNames.LogicalDelete or SystemFieldNames.CreatedAt or SystemFieldNames.UpdatedAt
                    or SystemFieldNames.DeletedAt or SystemFieldNames.Creator or SystemFieldNames.Updater) continue;
                yield return f.Name;
            }
        }

        /// <summary>1 フィールドの値を人が読む文字列に。空なら null。</summary>
        public static string? FormatValue(DesignData? design, FieldDesignBase fieldDesign, FieldDataBase data)
        {
            switch (data)
            {
                case PasswordFieldData:
                case SemanticSearchFieldData:
                case ListFieldData:
                case ModuleFieldData:
                    return null;
                case LinkFieldData link:
                    return string.IsNullOrEmpty(link.DisplayText) ? link.Value : link.DisplayText;
                case SelectFieldData select:
                    if (!string.IsNullOrEmpty(select.DisplayText)) return select.DisplayText;
                    return string.IsNullOrEmpty(select.Value) ? null : (CandidateDisplayText(design, fieldDesign as SelectFieldDesign, select.Value) ?? select.Value);
                case RichTextFieldData rich:
                    return rich.Value == null ? null : System.Net.WebUtility.HtmlDecode(_tags.Replace(rich.Value, " "));
                case FileFieldData file:
                    return file.FileName;
                case BooleanFieldData b:
                    return b.Value == null ? null : b.Value.Value ? Properties.Resources.SemanticSearchText_True : Properties.Resources.SemanticSearchText_False;
                case DateFieldData d:
                    return d.Value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case DateTimeFieldData dt:
                    return dt.Value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                case DateTimeOffsetFieldData dto:
                    return dto.Value?.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
                case TimeFieldData t:
                    return t.Value?.ToString("HH:mm", CultureInfo.InvariantCulture);
                case NumberFieldData n:
                    return n.Value?.ToString("0.############", CultureInfo.InvariantCulture);
            }
            //その他の値フィールド (Text / Markdown / RadioGroup / Id / 拡張フィールド) は Value をそのまま
            var value = data.GetType().GetProperty("Value")?.GetValue(data);
            return value switch
            {
                null => null,
                string s => s,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
        }

        //SelectField の候補値 (Enum の Members か Candidates "表示,値") から表示名を引く
        static string? CandidateDisplayText(DesignData? design, SelectFieldDesign? select, string value)
        {
            if (select == null) return null;
            if (!string.IsNullOrEmpty(select.EnumName) && design != null)
            {
                var e = design.Enums.FirstOrDefault(x => x.Name == select.EnumName);
                var member = e?.Members.FirstOrDefault(m => m.Value == value);
                if (member != null) return string.IsNullOrEmpty(member.DisplayText) ? member.Name : member.DisplayText;
            }
            foreach (var c in select.Candidates)
            {
                var parts = c.Split(',', 2);
                if (parts.Length == 2 && parts[1].Trim() == value) return parts[0].Trim();
                if (parts.Length == 1 && parts[0].Trim() == value) return value;
            }
            return null;
        }
    }
}
