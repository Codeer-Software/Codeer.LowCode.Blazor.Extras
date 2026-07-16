using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// <see cref="MappedFileTransferFieldDesign"/> による内部テーブルテキスト⇔外部ファイル列の相互変換。
    /// 列位置 = マッピング定義の並び順。コード変換表はただの業務モジュールで、
    /// LimitCount = null (全件) で読み込んで辞書にする。
    /// 通常は BulkFileTransfer 経由で使われるが、特殊実装 (独自の入出力経路) からも利用できる。
    /// </summary>
    public static class MappedFileTransform
    {
        /// <summary>出力: 内部テーブルテキスト → 外部列。</summary>
        public static async Task<List<List<string>>> ToExternalAsync(List<List<string>> internalTexts, MappedFileTransferFieldDesign design, ModuleDataIO moduleDataIO)
        {
            var converter = await CodeConverter.LoadAsync(design, moduleDataIO);
            var header = internalTexts.Count == 0 ? new() : internalTexts[0];
            var cols = design.Columns.Items;
            var fieldIndexes = cols.Select(c => header.IndexOf(c.Field)).ToList();

            var result = new List<List<string>>();
            if (design.HasHeader) result.Add(cols.Select(c => c.ExternalName).ToList());
            foreach (var row in internalTexts.Skip(1))
            {
                var outRow = new List<string>();
                for (var i = 0; i < cols.Count; i++)
                {
                    var c = cols[i];
                    var v = string.IsNullOrEmpty(c.Field)
                        ? c.FixedValue
                        : (0 <= fieldIndexes[i] && fieldIndexes[i] < row.Count ? row[fieldIndexes[i]] : string.Empty);
                    //コード変換 (内部→外部)。変換列に書式は適用しない
                    if (!string.IsNullOrEmpty(c.ConversionModule)) converter.TryToExternal(c, v, out v);
                    else v = ApplyFormat(v, c.Format);
                    outRow.Add(v);
                }
                result.Add(outRow);
            }
            return result;
        }

        /// <summary>
        /// 取込: 外部列 → 内部テーブルテキスト。コード変換で引き当てられなかった値は errors に行番号付きで報告する。
        /// </summary>
        public static async Task<(List<List<string>> Texts, List<string> Errors)> ToInternalAsync(
            List<List<string>> externalTexts, MappedFileTransferFieldDesign design, ModuleDataIO moduleDataIO)
        {
            var converter = await CodeConverter.LoadAsync(design, moduleDataIO);
            var cols = design.Columns.Items;
            //取込対象 (Field 指定あり) の列だけ内部ヘッダにする
            var mapped = cols.Select((c, i) => (Column: c, FileIndex: i)).Where(e => !string.IsNullOrEmpty(e.Column.Field)).ToList();

            var errors = new List<string>();
            var result = new List<List<string>> { mapped.Select(e => e.Column.Field).ToList() };
            var dataRows = design.HasHeader ? externalTexts.Skip(1) : externalTexts;
            var fileRow = design.HasHeader ? 1 : 0;
            foreach (var row in dataRows)
            {
                fileRow++;
                var outRow = new List<string>();
                foreach (var e in mapped)
                {
                    var v = e.FileIndex < row.Count ? row[e.FileIndex] : string.Empty;
                    if (!string.IsNullOrEmpty(e.Column.ConversionModule))
                    {
                        //コード変換 (外部→内部)。引き当てられない外部コードはエラー
                        if (!converter.TryToInternal(e.Column, v, out v) && !string.IsNullOrEmpty(v))
                            errors.Add($"Row {fileRow}, {ColumnLabel(e.Column)}: code '{v}' was not found in '{e.Column.ConversionModule}'.");
                    }
                    else
                    {
                        v = ParseFormat(v, e.Column.Format);
                    }
                    outRow.Add(v);
                }
                result.Add(outRow);
            }
            return (result, errors);
        }

        static string ColumnLabel(MappingColumn c) => string.IsNullOrEmpty(c.ExternalName) ? c.Field : c.ExternalName;

        //出力書式: 日付/数値としてパースできれば書式を適用、できなければそのまま
        //(内部値は ToTextData の ToString() 産なので同じサーバーカルチャでパースする)
        static string ApplyFormat(string value, string format)
        {
            if (string.IsNullOrEmpty(format) || string.IsNullOrEmpty(value)) return value;
            if (DateTime.TryParse(value, out var d)) return d.ToString(format, CultureInfo.InvariantCulture);
            if (decimal.TryParse(value, out var n)) return n.ToString(format, CultureInfo.InvariantCulture);
            return value;
        }

        //取込書式: 書式どおりの日付なら標準形式 (ISO) へ。それ以外はそのまま (前ゼロ数値等は素で変換可能)
        static string ParseFormat(string value, string format)
        {
            if (string.IsNullOrEmpty(format) || string.IsNullOrEmpty(value)) return value;
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm:ss");
            return value;
        }

        /// <summary>コード変換表 (業務モジュール) を読み込んだ双方向辞書。</summary>
        class CodeConverter
        {
            readonly Dictionary<string, (Dictionary<string, string> ToExternal, Dictionary<string, string> ToInternal)> _tables = new();

            internal static async Task<CodeConverter> LoadAsync(MappedFileTransferFieldDesign design, ModuleDataIO moduleDataIO)
            {
                var converter = new CodeConverter();
                foreach (var c in design.Columns.Items)
                {
                    if (string.IsNullOrEmpty(c.ConversionModule)) continue;
                    var key = TableKey(c);
                    if (converter._tables.ContainsKey(key)) continue;

                    //LimitCount = null で全件。変換表は小さい前提
                    var texts = await moduleDataIO.GetTableTextsAsync(new SearchCondition { ModuleName = c.ConversionModule });
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
