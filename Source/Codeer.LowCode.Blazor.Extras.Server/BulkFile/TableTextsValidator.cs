using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// 一括更新テキスト (GetTableTextsAsync 形式) の取込前チェック。
    /// 「対応しない列名 (取込で黙って無視される)」と「明らかに型変換できないセル (取込で黙って null になる)」を
    /// 行番号付きで報告する。列の対応付けは取込本体と同じ規約 (FieldName.DataMemberName) を、
    /// 公開の <see cref="DbColumnAttribute"/> (デザインの DbColumn プロパティに付与) から再構成する。
    /// 確信の持てない型はチェックしない (偽陽性を出さない)。ここをすり抜けた不正データは
    /// 取込本体の例外とトランザクションロールバックが最終安全網になる。
    /// </summary>
    public static class TableTextsValidator
    {
        /// <summary>検証してエラーメッセージ (行番号付き) を返す。エラーなしなら空。</summary>
        public static List<string> Validate(DesignData designData, string? moduleName, List<List<string>> texts)
        {
            var errors = new List<string>();
            var design = designData.Modules.Find(moduleName ?? string.Empty);
            if (design == null || texts.Count == 0) return errors;
            var header = texts[0];

            //有効なヘッダ (FieldName.DataMemberName) と型チェック対象の列を集める
            var knownHeaders = new HashSet<string>();
            var headerToType = new Dictionary<string, Type>();
            foreach (var field in design.Fields)
            {
                var dataType = field.CreateData()?.GetType();
                //DisableBulkDataUpdate のフィールド (File 等) は取込対象外 = ヘッダとしては正しいが型チェックしない
                var checkable = dataType != null &&
                    field.GetType().GetCustomAttribute<DisableBulkDataUpdateAttribute>() == null;
                foreach (var prop in field.GetType().GetProperties())
                {
                    var attr = prop.GetCustomAttribute<DbColumnAttribute>();
                    if (attr == null) continue;
                    if (string.IsNullOrEmpty(prop.GetValue(field) as string)) continue; //DB列未割当
                    var h = $"{field.Name}.{attr.DataMember}";
                    knownHeaders.Add(h);
                    var memberType = checkable ? dataType!.GetProperty(attr.DataMember)?.PropertyType : null;
                    if (memberType != null) headerToType[h] = memberType;
                }
            }

            //ヘッダ行: 対応しない列名 (取込時に黙って無視される)
            foreach (var h in header)
            {
                if (string.IsNullOrEmpty(h)) continue;
                if (!knownHeaders.Contains(h)) errors.Add($"Row 1: unknown column '{h}'.");
            }

            //データ行: 明らかに型変換できないセル (取込時に黙って null になる)
            for (var r = 1; r < texts.Count; r++)
            {
                var row = texts[r];
                for (var i = 0; i < row.Count && i < header.Count; i++)
                {
                    var cell = row[i];
                    if (string.IsNullOrEmpty(cell)) continue;
                    if (!headerToType.TryGetValue(header[i], out var type)) continue;
                    if (!CanConvert(cell, type)) errors.Add($"Row {r + 1}, {header[i]}: cannot convert '{cell}'.");
                }
            }
            return errors;
        }

        //確実に判定できる型だけチェックする (それ以外は取込本体に任せる)
        static bool CanConvert(string cell, Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            if (t == typeof(string)) return true;
            if (t == typeof(decimal)) return decimal.TryParse(cell, out _);
            if (t == typeof(double)) return double.TryParse(cell, out _);
            if (t == typeof(float)) return float.TryParse(cell, out _);
            if (t == typeof(long)) return long.TryParse(cell, out _);
            if (t == typeof(int)) return int.TryParse(cell, out _);
            if (t == typeof(bool)) return bool.TryParse(cell, out _) || cell is "0" or "1";
            if (t == typeof(DateTime)) return DateTime.TryParse(cell, out _);
            if (t == typeof(DateOnly)) return DateOnly.TryParse(cell, out _) || DateTime.TryParse(cell, out _);
            if (t == typeof(TimeOnly)) return TimeOnly.TryParse(cell, out _);
            if (t == typeof(TimeSpan)) return TimeSpan.TryParse(cell, out _) || TimeOnly.TryParse(cell, out _);
            if (t == typeof(Guid)) return Guid.TryParse(cell, out _);
            return true; //未知の型はチェックしない
        }
    }
}
