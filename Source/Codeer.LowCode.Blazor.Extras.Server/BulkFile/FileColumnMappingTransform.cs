using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// <see cref="FileColumnMappingFieldDesign"/> による ModuleData ⇔ 外部ファイル列の相互変換。
    /// テーブルテキストを経由せず型付きの値で変換する。列位置 = マッピング定義の並び順。
    /// 書式変換はフィールドデザインに委譲する (IExternalTextFormatFieldDesign。標準では日付/日時/数値
    /// フィールドが実装し、書式は各フィールドの Format プロパティ。実装しない型は ToString / 型変換のみ)。
    /// コード変換表はただの業務モジュールで、LimitCount = null (全件) で読み込んで辞書にする。
    /// 通常は BulkFileTransfer 経由で使われるが、特殊実装 (独自の入出力経路) からは
    /// getTableTexts を差し替えるオーバーロードも利用できる。
    /// </summary>
    public static class FileColumnMappingTransform
    {
        /// <summary>出力: ModuleData → 外部列。</summary>
        public static Task<List<List<string>>> ToExternalAsync(List<ModuleData> items,
            FileColumnMappingFieldDesign design, ModuleDesign moduleDesign, ModuleDataIO moduleDataIO)
            => ToExternalAsync(items, design, moduleDesign, moduleDataIO.GetTableTextsAsync);

        /// <summary>出力: ModuleData → 外部列 (変換表の取得手段を差し替え可能)。</summary>
        public static async Task<List<List<string>>> ToExternalAsync(List<ModuleData> items,
            FileColumnMappingFieldDesign design, ModuleDesign moduleDesign,
            Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
        {
            var converter = await CodeConverter.LoadAsync(design, getTableTexts);
            var cols = design.Columns.Items;
            var targets = cols.Select(c => MappingTarget.Create(moduleDesign, c)).ToList();

            var result = new List<List<string>>();
            if (design.HasHeader) result.Add(cols.Select(c => c.ExternalName).ToList());
            foreach (var item in items)
            {
                var outRow = new List<string>();
                for (var i = 0; i < cols.Count; i++)
                {
                    var c = cols[i];
                    var target = targets[i];
                    string v;
                    if (target == null)
                    {
                        v = c.FixedValue;
                    }
                    else if (!string.IsNullOrEmpty(c.ConversionModule))
                    {
                        //コード変換 (内部→外部)。変換列に書式は適用しない
                        v = target.GetValue(item)?.ToString() ?? string.Empty;
                        converter.TryToExternal(c, v, out v);
                    }
                    else if (target.FieldDesign is IExternalTextFormatFieldDesign f)
                    {
                        v = f.FormatExternalText(target.GetValue(item));
                    }
                    else
                    {
                        v = target.GetValue(item)?.ToString() ?? string.Empty;
                    }
                    outRow.Add(v);
                }
                result.Add(outRow);
            }
            return result;
        }

        /// <summary>取込: 外部列 → ModuleData。</summary>
        public static Task<(List<ModuleData> Items, List<string> Errors)> ToInternalAsync(
            List<List<string>> externalTexts, FileColumnMappingFieldDesign design, ModuleDesign moduleDesign,
            ModuleDataIO moduleDataIO)
            => ToInternalAsync(externalTexts, design, moduleDesign, moduleDataIO.GetTableTextsAsync);

        /// <summary>
        /// 取込: 外部列 → ModuleData (変換表の取得手段を差し替え可能)。
        /// コード変換で引き当てられなかった値・書式や型として解釈できなかった値は errors に行番号付きで報告する。
        /// </summary>
        public static async Task<(List<ModuleData> Items, List<string> Errors)> ToInternalAsync(
            List<List<string>> externalTexts, FileColumnMappingFieldDesign design, ModuleDesign moduleDesign,
            Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
        {
            var converter = await CodeConverter.LoadAsync(design, getTableTexts);
            //取込対象 = Field 指定があり、一括入出力可能なフィールドに解決できた列
            var mapped = design.Columns.Items
                .Select((c, i) => (Column: c, FileIndex: i, Target: MappingTarget.Create(moduleDesign, c)))
                .Where(e => e.Target != null)
                .ToList();

            var errors = new List<string>();
            var items = new List<ModuleData>();
            var dataRows = design.HasHeader ? externalTexts.Skip(1) : externalTexts;
            var fileRow = design.HasHeader ? 1 : 0;
            foreach (var row in dataRows)
            {
                fileRow++;
                var moduleData = new ModuleData { Name = moduleDesign.Name };
                foreach (var e in mapped)
                {
                    var text = e.FileIndex < row.Count ? row[e.FileIndex] : string.Empty;
                    var target = e.Target!;

                    object? value;
                    if (!string.IsNullOrEmpty(e.Column.ConversionModule))
                    {
                        //コード変換 (外部→内部)。引き当てられない外部コードはエラー
                        if (!converter.TryToInternal(e.Column, text, out var internalText) && !string.IsNullOrEmpty(text))
                        {
                            errors.Add($"Row {fileRow}, {ColumnLabel(e.Column)}: code '{text}' was not found in '{e.Column.ConversionModule}'.");
                            continue;
                        }
                        if (!target.TryConvert(internalText, out value))
                        {
                            errors.Add($"Row {fileRow}, {ColumnLabel(e.Column)}: cannot convert '{internalText}'.");
                            continue;
                        }
                    }
                    else if (target.FieldDesign is IExternalTextFormatFieldDesign f)
                    {
                        //フィールドの書式変換 (外部→値)。書式どおりに解釈できない値はエラー
                        if (!f.TryParseExternalText(text, out value))
                        {
                            if (!string.IsNullOrEmpty(text))
                                errors.Add($"Row {fileRow}, {ColumnLabel(e.Column)}: cannot parse '{text}'.");
                            continue;
                        }
                    }
                    else
                    {
                        //書式を持たないフィールドは型変換のみ。変換できない値はエラー
                        if (!target.TryConvert(text, out value))
                        {
                            errors.Add($"Row {fileRow}, {ColumnLabel(e.Column)}: cannot convert '{text}'.");
                            continue;
                        }
                    }
                    target.SetValue(moduleData, value);
                }
                items.Add(moduleData);
            }
            return (items, errors);
        }

        static string ColumnLabel(MappingColumn c) => string.IsNullOrEmpty(c.ExternalName) ? c.Field : c.ExternalName;

        /// <summary>
        /// マッピング列の入出力先 ("フィールド名.データメンバ名" を解決したフィールドデザインとデータメンバ)。
        /// メンバ名省略時は "Value"。一括入出力不可 (DisableBulkDataUpdate) のフィールドは対象外。
        /// </summary>
        class MappingTarget
        {
            readonly FieldDesignBase _fieldDesign;
            readonly PropertyInfo _property;

            MappingTarget(FieldDesignBase fieldDesign, PropertyInfo property)
            {
                _fieldDesign = fieldDesign;
                _property = property;
            }

            internal FieldDesignBase FieldDesign => _fieldDesign;

            internal static MappingTarget? Create(ModuleDesign moduleDesign, MappingColumn col)
            {
                if (string.IsNullOrEmpty(col.Field)) return null;
                var sp = col.Field.Split('.', 2);
                var fieldDesign = moduleDesign.Fields.FirstOrDefault(f => f.Name == sp[0]);
                if (fieldDesign == null) return null;
                if (fieldDesign.GetType().GetCustomAttribute<DisableBulkDataUpdateAttribute>() != null) return null;
                var property = fieldDesign.CreateData()?.GetType().GetProperty(sp.Length == 2 ? sp[1] : "Value");
                if (property == null) return null;
                return new MappingTarget(fieldDesign, property);
            }

            internal object? GetValue(ModuleData item)
                => item.Fields.TryGetValue(_fieldDesign.Name, out var data) ? _property.GetValue(data) : null;

            internal void SetValue(ModuleData item, object? value)
            {
                if (!item.Fields.TryGetValue(_fieldDesign.Name, out var data))
                {
                    data = _fieldDesign.CreateData();
                    if (data == null) return;
                    item.Fields[_fieldDesign.Name] = data;
                }
                _property.SetValue(data, value);
            }

            //テキスト → データメンバの型 (テキスト経路 (Excel/CSV) と同じ変換規約)
            internal bool TryConvert(string text, out object? value)
                => BulkDataTextConverter.TryConvert(text, _property.PropertyType, out value);
        }

        /// <summary>コード変換表 (業務モジュール) を読み込んだ双方向辞書。</summary>
        class CodeConverter
        {
            readonly Dictionary<string, (Dictionary<string, string> ToExternal, Dictionary<string, string> ToInternal)> _tables = new();

            internal static async Task<CodeConverter> LoadAsync(FileColumnMappingFieldDesign design,
                Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
            {
                var converter = new CodeConverter();
                foreach (var c in design.Columns.Items)
                {
                    if (string.IsNullOrEmpty(c.ConversionModule)) continue;
                    var key = TableKey(c);
                    if (converter._tables.ContainsKey(key)) continue;

                    //LimitCount = null で全件。変換表は小さい前提
                    var texts = await getTableTexts(new SearchCondition { ModuleName = c.ConversionModule });
                    var header = texts.Count == 0 ? new() : texts[0];
                    var extIndex = FindColumn(header, c.ConversionExternalField);
                    var intIndex = FindColumn(header, c.ConversionInternalField);

                    var toExternal = new Dictionary<string, string>();
                    var toInternal = new Dictionary<string, string>();
                    if (0 <= extIndex && 0 <= intIndex)
                    {
                        foreach (var row in texts.Skip(1))
                        {
                            if (row.Count <= extIndex || row.Count <= intIndex) continue;
                            toExternal[row[intIndex]] = row[extIndex];
                            toInternal[row[extIndex]] = row[intIndex];
                        }
                    }
                    converter._tables[key] = (toExternal, toInternal);
                }
                return converter;
            }

            //フィールド名だけ ("EdiCode") でも内部名ヘッダ ("EdiCode.Value") でも受け付ける
            static int FindColumn(List<string> header, string fieldName)
            {
                if (string.IsNullOrEmpty(fieldName)) return -1;
                var exact = header.IndexOf(fieldName);
                if (0 <= exact) return exact;
                return header.FindIndex(h => h.StartsWith(fieldName + ".", StringComparison.Ordinal));
            }

            static string TableKey(MappingColumn c) => $"{c.ConversionModule}|{c.ConversionExternalField}|{c.ConversionInternalField}";

            internal bool TryToExternal(MappingColumn c, string value, out string converted)
                => TryConvert(c, value, e => e.ToExternal, out converted);

            internal bool TryToInternal(MappingColumn c, string value, out string converted)
                => TryConvert(c, value, e => e.ToInternal, out converted);

            bool TryConvert(MappingColumn c, string value,
                Func<(Dictionary<string, string> ToExternal, Dictionary<string, string> ToInternal), Dictionary<string, string>> selector,
                out string converted)
            {
                converted = value;
                if (!_tables.TryGetValue(TableKey(c), out var table)) return false;
                if (!selector(table).TryGetValue(value, out var v)) return false;
                converted = v;
                return true;
            }
        }
    }
}
