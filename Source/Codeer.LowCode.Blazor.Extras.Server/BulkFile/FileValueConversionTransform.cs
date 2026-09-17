using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.BulkFile;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// 標準形式 (内部名ヘッダ "フィールド名.データメンバ名" のテーブルテキスト) に <see cref="FileValueConversionFieldDesign"/> を適用する。
    /// 見出しは変えず、変換対象フィールドの "フィールド名.Value" 列のセルの値だけを外部値⇔内部値に引き当てる
    /// (標準形式の見出し照合・列の並び替え/省略はそのまま活きる)。
    /// FileColumnMappingField 併用時の変換は <see cref="FileColumnMappingTransform"/> が同じ変換表 (FileValueConverter) で行う。
    /// 引数の texts は書き換えて返す。
    /// </summary>
    public static class FileValueConversionTransform
    {
        /// <summary>出力: 変換対象列の内部値 → 外部値。引き当てられない内部値はそのまま出す。空セルは空のまま。</summary>
        public static Task<List<List<string>>> ToExternalAsync(List<List<string>> texts, ModuleDesign moduleDesign, ModuleDataIO moduleDataIO)
            => ToExternalAsync(texts, moduleDesign, c => moduleDataIO.GetTableTextsAsync(c)); //変換フィールドが無ければ呼ばれない (moduleDataIO 無しの解析経路を許す)

        /// <summary>出力: 変換対象列の内部値 → 外部値 (変換表の取得手段を差し替え可能)。</summary>
        public static async Task<List<List<string>>> ToExternalAsync(List<List<string>> texts, ModuleDesign moduleDesign,
            Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
        {
            var converter = await FileValueConverter.LoadAsync(moduleDesign, getTableTexts);
            if (converter.IsEmpty || texts.Count == 0) return texts;

            var columns = TargetColumns(texts[0], converter);
            foreach (var row in texts.Skip(1))
            {
                foreach (var (index, field) in columns)
                {
                    if (row.Count <= index || string.IsNullOrEmpty(row[index])) continue;
                    if (converter.TryToExternal(field, row[index], out var v)) row[index] = v;
                }
            }
            return texts;
        }

        /// <summary>
        /// 取込: 変換対象列の外部値 → 内部値。引き当てられない外部値は行番号付きエラー ("Row N, 列名: code 'X' was not found in 'モジュール'.")。
        /// 空セル (空白だけも含む) は空 (= 取込で null) にする。
        /// </summary>
        public static Task<(List<List<string>> Texts, List<string> Errors)> ToInternalAsync(List<List<string>> texts, ModuleDesign moduleDesign, ModuleDataIO moduleDataIO)
            => ToInternalAsync(texts, moduleDesign, c => moduleDataIO.GetTableTextsAsync(c)); //変換フィールドが無ければ呼ばれない (moduleDataIO 無しの解析経路を許す)

        /// <summary>取込: 変換対象列の外部値 → 内部値 (変換表の取得手段を差し替え可能)。</summary>
        public static async Task<(List<List<string>> Texts, List<string> Errors)> ToInternalAsync(List<List<string>> texts, ModuleDesign moduleDesign,
            Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
        {
            var (converted, cellErrors) = await ToInternalWithCellErrorsAsync(texts, moduleDesign, getTableTexts);
            return (converted, cellErrors.Select(e => $"Row {e.FileRow}, {e.ColumnLabel}: {e.Message}").ToList());
        }

        /// <summary>取込: 変換対象列の外部値 → 内部値 (セル単位の構造化エラー。parse_file / BulkFileReader 用)。</summary>
        public static Task<(List<List<string>> Texts, List<BulkFileCellError> Errors)> ToInternalWithCellErrorsAsync(List<List<string>> texts, ModuleDesign moduleDesign, ModuleDataIO moduleDataIO)
            => ToInternalWithCellErrorsAsync(texts, moduleDesign, c => moduleDataIO.GetTableTextsAsync(c)); //変換フィールドが無ければ呼ばれない (moduleDataIO 無しの解析経路を許す)

        /// <summary>
        /// 取込: 変換対象列の外部値 → 内部値 (セル単位の構造化エラー。変換表の取得手段を差し替え可能)。
        /// 引き当てられないセルは空 (値未設定) にしてエラーに載せ、行自体は捨てない。
        /// </summary>
        public static async Task<(List<List<string>> Texts, List<BulkFileCellError> Errors)> ToInternalWithCellErrorsAsync(List<List<string>> texts, ModuleDesign moduleDesign,
            Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
        {
            var errors = new List<BulkFileCellError>();
            var converter = await FileValueConverter.LoadAsync(moduleDesign, getTableTexts);
            if (converter.IsEmpty || texts.Count == 0) return (texts, errors);

            var header = texts[0];
            var columns = TargetColumns(header, converter);
            for (var r = 1; r < texts.Count; r++)
            {
                var row = texts[r];
                foreach (var (index, field) in columns)
                {
                    if (row.Count <= index) continue;
                    var text = row[index];
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        //空セル (空白だけも含む) は引き当てず未設定 (Id 付き更新では参照を外す)
                        row[index] = string.Empty;
                        continue;
                    }
                    if (converter.TryToInternal(field, text, out var v))
                    {
                        row[index] = v;
                        continue;
                    }
                    errors.Add(new BulkFileCellError
                    {
                        ItemIndex = r - 1,
                        FileRow = r + 1,
                        FieldName = field,
                        ColumnLabel = header[index],
                        Message = $"code '{text}' was not found in '{converter.ModuleNameOf(field)}'."
                    });
                    row[index] = string.Empty;
                }
            }
            return (texts, errors);
        }

        //変換対象 = 見出しが "変換対象フィールド名.Value" (データメンバ省略は Value) の列
        static List<(int Index, string Field)> TargetColumns(List<string> header, FileValueConverter converter)
        {
            var columns = new List<(int, string)>();
            for (var i = 0; i < header.Count; i++)
            {
                var sp = header[i].Split('.', 2);
                if (sp.Length == 2 && sp[1] != "Value") continue;
                if (converter.Converts(sp[0])) columns.Add((i, sp[0]));
            }
            return columns;
        }
    }
}
