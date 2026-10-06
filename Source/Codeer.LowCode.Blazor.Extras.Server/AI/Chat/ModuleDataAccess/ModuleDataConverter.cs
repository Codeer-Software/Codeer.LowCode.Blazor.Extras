using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// AI が書いた条件 (項目名・比較・値) を本体の検索条件 (<see cref="SearchCondition"/>) に、読んだ行 (<see cref="ModuleData"/>) を AI に返す JSON 向けの値に変換する。
    /// 項目はフィールド名で指す (リンク先は "Link.Field")。本体の変数名 ("Field.Value") への変換と、フィールドの型に合わせた値の変換をここで行う。
    /// </summary>
    internal static class ModuleDataConverter
    {
        const string ValueSuffix = ".Value";

        /// <summary>フィールド名 (または "Link.Field"・"Field.Value") を本体の検索変数名 "Field.Value" にする。</summary>
        public static string ToVariable(string field)
        {
            var name = (field ?? string.Empty).Trim();
            return name.EndsWith(ValueSuffix, StringComparison.Ordinal) ? name : name + ValueSuffix;
        }

        /// <summary>検索変数名からフィールド名に戻す ("Field.Value" → "Field")。</summary>
        public static string ToFieldName(string variable)
        {
            var name = (variable ?? string.Empty).Trim();
            return name.EndsWith(ValueSuffix, StringComparison.Ordinal) ? name[..^ValueSuffix.Length] : name;
        }

        /// <summary>
        /// 項目のパス ("Field" / "Link.Field") が指すフィールドのデザイン。モジュールにその名前のリンク越しフィールド ("Link.Field" を名前に持つ列) があればそれ、
        /// 無ければリンクを辿って相手モジュールのフィールド。見つからなければ null。
        /// </summary>
        public static FieldDesignBase? FindField(DesignData design, ModuleDesign module, string field)
        {
            var name = ToFieldName(field);
            var exact = module.Fields.FirstOrDefault(f => f.Name == name);
            if (exact != null) return exact;
            var segments = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            ModuleDesign? current = module;
            FieldDesignBase? found = null;
            foreach (var segment in segments)
            {
                if (current == null) return null;
                found = current.Fields.FirstOrDefault(f => f.Name == segment);
                if (found == null) return null;
                current = found is LinkFieldDesign link && !string.IsNullOrEmpty(link.SearchCondition.ModuleName)
                    ? design.Modules.Find(link.SearchCondition.ModuleName)
                    : null;
            }
            return found;
        }

        /// <summary>
        /// AI が渡した値 (JSON) を、フィールドの型に合わせた <see cref="MultiTypeValue"/> にする。配列は In / NotIn 用のリスト。
        /// 型が分からない (フィールドが見つからない) ときは JSON の種類から決める。変換できなければ例外 (メッセージは AI に返す)。
        /// </summary>
        public static MultiTypeValue ToMatchValue(JsonElement value, FieldDesignBase? field)
        {
            if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return new NullValue();
            if (value.ValueKind == JsonValueKind.Array)
            {
                var items = value.EnumerateArray().Select(e => ToMatchValue(e, field).GetValue()).Where(v => v != null).Select(v => v!).ToList();
                if (items.Count == 0) return new NullValue();
                return MultiTypeValue.Create(items);
            }
            switch (field)
            {
                case NumberFieldDesign:
                    return MultiTypeValue.Create(ToDecimal(value));
                case BooleanFieldDesign:
                    return MultiTypeValue.Create(ToBoolean(value));
                case DateFieldDesign:
                    return MultiTypeValue.Create(DateOnly.Parse(Text(value), CultureInfo.InvariantCulture));
                case DateTimeFieldDesign:
                    return MultiTypeValue.Create(DateTime.Parse(Text(value), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal));
                case TimeFieldDesign:
                    return MultiTypeValue.Create(TimeOnly.Parse(Text(value), CultureInfo.InvariantCulture));
                case null:
                    return value.ValueKind switch
                    {
                        JsonValueKind.Number => MultiTypeValue.Create(value.GetDecimal()),
                        JsonValueKind.True or JsonValueKind.False => MultiTypeValue.Create(value.GetBoolean()),
                        _ => MultiTypeValue.Create(Text(value)),
                    };
                default:
                    return MultiTypeValue.Create(Text(value));
            }
        }

        static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();

        static decimal ToDecimal(JsonElement value)
            => value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : decimal.Parse(Text(value), NumberStyles.Any, CultureInfo.InvariantCulture);

        static bool ToBoolean(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDecimal() != 0,
            _ => Text(value).Trim().ToLowerInvariant() is "true" or "1" or "yes",
        };

        /// <summary>比較の名前 (Equal / NotEqual / LessThan / ... / Like / In / NotIn。大文字小文字は区別しない。記号も可) を列挙に。分からなければ null。</summary>
        public static MatchComparison? ParseComparison(string? comparison)
        {
            if (string.IsNullOrWhiteSpace(comparison)) return MatchComparison.Equal;
            var name = comparison.Trim();
            foreach (var c in Enum.GetValues<MatchComparison>())
            {
                if (string.Equals(c.ToString(), name, StringComparison.OrdinalIgnoreCase)) return c;
            }
            if (string.Equals(name, "contains", StringComparison.OrdinalIgnoreCase)) return MatchComparison.Like;
            return name switch
            {
                "=" or "==" => MatchComparison.Equal,
                "!=" or "<>" => MatchComparison.NotEqual,
                "<" => MatchComparison.LessThan,
                "<=" => MatchComparison.LessThanOrEqual,
                ">" => MatchComparison.GreaterThan,
                ">=" => MatchComparison.GreaterThanOrEqual,
                _ => null,
            };
        }

        /// <summary>
        /// 1 行を AI に返す形 (項目名 → 値) にする。選択・リンクは値と表示名 ({ value, text })、日付は ISO 風の文字列、
        /// ファイルはファイル名。子一覧 (ListField / ModuleField)・パスワード・楽観ロック・JSON は出さない。
        /// 読み取り権限の無い項目は本体が先に落としているので、ここにはそもそも来ない。
        /// </summary>
        public static Dictionary<string, object?> ToRow(DesignData? design, ModuleDesign? module, ModuleData data)
        {
            var row = new Dictionary<string, object?>();
            foreach (var (name, field) in data.Fields)
            {
                switch (field)
                {
                    case null:
                    case PasswordFieldData:
                    case OptimisticLockingFieldData:
                    case ListFieldData:
                    case ModuleFieldData:
                    case JsonFieldData:
                        continue;
                    case FileFieldData file:
                        row[name] = file.FileName;
                        continue;
                    case SelectFieldData select:
                        row[name] = WithText(select.Value, FirstText(select.DisplayText, LinkedText(design, module, name, data), CandidateText(design, module, name, select.Value)));
                        continue;
                    case LinkFieldData link:
                        row[name] = WithText(link.Value, FirstText(link.DisplayText, LinkedText(design, module, name, data)));
                        continue;
                }
                row[name] = ToJsonValue(RawValue(field));
            }
            return row;
        }

        static string? FirstText(params string?[] candidates) => candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c));

        /// <summary>
        /// リンク (と、モジュールを参照する選択) の表示名。本体はリンク越しフィールド ("Customer.Name") を一緒に読んだときだけ値を返し、
        /// LinkFieldData.DisplayText は埋めない (画面側がリンク越しフィールドから表示する) ので、同じ行のそのフィールドの値を表示名にする。
        /// リンク越しフィールドのパスは <see cref="DisplayTextPath"/>。
        /// </summary>
        static string? LinkedText(DesignData? design, ModuleDesign? module, string fieldName, ModuleData data)
        {
            var path = design == null || module == null ? null : DisplayTextPath(design, module, fieldName);
            if (path == null || !data.Fields.TryGetValue(path, out var linked)) return null;
            return Convert.ToString(RawValue(linked), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// リンク (またはモジュールを参照する選択) の表示名を持つリンク越しフィールドの名前 ("Customer.Name")。モジュールにそのフィールドが無ければ null
        /// (本体は設計に無いリンク越しフィールドを読めない。デザイナでリンク先の項目をレイアウトに置くと作られる)。
        /// </summary>
        public static string? DisplayTextPath(DesignData design, ModuleDesign module, string fieldName)
        {
            var (targetModule, displayText) = module.Fields.FirstOrDefault(f => f.Name == fieldName) switch
            {
                LinkFieldDesign link => (link.SearchCondition.ModuleName, link.DisplayTextVariable),
                SelectFieldDesign select => (select.SearchCondition.ModuleName, select.DisplayTextVariable),
                _ => (string.Empty, string.Empty),
            };
            if (string.IsNullOrEmpty(targetModule) || string.IsNullOrEmpty(displayText)) return null;
            var path = fieldName + "." + ToFieldName(displayText);
            return module.Fields.Any(f => f.Name == path) ? path : null;
        }

        /// <summary>値と表示名 ({ value, text })。表示名が無いか値と同じなら値だけ。</summary>
        public static object? WithText(object? value, string? text)
            => string.IsNullOrEmpty(text) || text == Convert.ToString(value, CultureInfo.InvariantCulture) ? value : new Dictionary<string, object?> { ["value"] = value, ["text"] = text };

        /// <summary>候補値つき SelectField の表示名 (サーバーは DisplayText を埋めないので設計から引く)。無ければ null。</summary>
        public static string? CandidateText(DesignData? design, ModuleDesign? module, string fieldName, string? value)
        {
            if (design == null || module == null || value == null) return null;
            var field = FindField(design, module, fieldName);
            if (field == null) return null;
            foreach (var candidate in DesignDescriber.Candidates(design, field))
            {
                //"値=表示" の形
                var index = candidate.IndexOf('=');
                if (index > 0 && candidate[..index] == value) return candidate[(index + 1)..];
            }
            return null;
        }

        /// <summary>値フィールドの生の値 (ValueFieldDataBase&lt;T&gt;.Value)。値を持たない型は null。</summary>
        public static object? RawValue(FieldDataBase? field)
        {
            if (field == null) return null;
            var property = field.GetType().GetProperty("Value");
            return property?.GetValue(field);
        }

        /// <summary>JSON に入れる形 (日付・時刻は文字列、数値はそのまま)。</summary>
        public static object? ToJsonValue(object? value) => value switch
        {
            null => null,
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            TimeOnly t => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TimeSpan t => t.ToString(),
            byte[] b => $"<binary {b.Length} bytes>",
            Guid g => g.ToString(),
            _ => value,
        };

        /// <summary>集計関数の名前 (count / countDistinct / sum / avg / min / max。大文字小文字は区別しない) を列挙に。分からなければ null。</summary>
        public static AggregateFunction? ParseFunction(string? function)
        {
            var name = (function ?? string.Empty).Trim().Replace("_", string.Empty);
            if (name.Length == 0) return AggregateFunction.Count;
            if (string.Equals(name, "average", StringComparison.OrdinalIgnoreCase)) return AggregateFunction.Avg;
            foreach (var f in Enum.GetValues<AggregateFunction>())
            {
                if (string.Equals(f.ToString(), name, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        /// <summary>日付の単位 (year / quarter / month / week / day / hour) を列挙に。分からなければ null。</summary>
        public static DateBucket? ParseBucket(string? unit)
        {
            var name = (unit ?? string.Empty).Trim();
            foreach (var b in Enum.GetValues<DateBucket>())
            {
                if (string.Equals(b.ToString(), name, StringComparison.OrdinalIgnoreCase)) return b;
            }
            return null;
        }

        /// <summary>
        /// 集計結果の軸の鍵を AI に返す形に。日付の軸は { value: 期間の開始日, text: 単位に応じた見出し } (月 "2026-04"・四半期 "FY2026 Q1" 等)、
        /// 選択・リンクは表示名があれば { value, text }、それ以外は値そのまま。空値は null。
        /// </summary>
        public static object? KeyToJson(AggregateKey key, AggregateGroup group, DesignData? design, ModuleDesign? module)
        {
            var raw = key.Value.GetValue();
            if (raw == null) return null;
            if (group is DateGroup dateGroup)
            {
                var start = raw switch
                {
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    DateTime d => d,
                    _ => (DateTime?)null,
                };
                if (start != null) return WithText(ToJsonValue(raw), PeriodText(start.Value, dateGroup.Bucket, dateGroup.FiscalYearStartMonth));
            }
            var text = key.DisplayText;
            if (string.IsNullOrEmpty(text) && design != null && module != null)
                text = CandidateText(design, module, ToFieldName(group.Variable), Convert.ToString(raw, CultureInfo.InvariantCulture));
            return WithText(ToJsonValue(raw), text);
        }

        /// <summary>期間の開始から見出し。年・四半期は年度の開始月が 1 以外なら "FY2026" / "FY2026 Q1" (年度は開始日の年)。</summary>
        public static string PeriodText(DateTime start, DateBucket bucket, int fiscalYearStartMonth)
        {
            var fiscalStart = fiscalYearStartMonth is < 1 or > 12 ? 1 : fiscalYearStartMonth;
            var fiscalYear = start.Month >= fiscalStart ? start.Year : start.Year - 1;
            var yearText = fiscalStart == 1 ? start.Year.ToString(CultureInfo.InvariantCulture) : "FY" + fiscalYear.ToString(CultureInfo.InvariantCulture);
            return bucket switch
            {
                DateBucket.Year => yearText,
                DateBucket.Quarter => $"{yearText} Q{(start.Month - fiscalStart + 12) % 12 / 3 + 1}",
                DateBucket.Month => start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                DateBucket.Week => start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "~",
                DateBucket.Day => start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            };
        }
    }
}
