using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// モジュールの <see cref="FileValueConversionFieldDesign"/> を読み込んだ、変換対象フィールドごとの双方向辞書。
    /// 変換表はただの業務モジュールで、LimitCount = null (全件) で読み込む (変換表は小さい前提。検索は現在ユーザーの読み取り権限で行われる)。
    /// 同じ値が複数行に一致する場合は先頭の行を採る。
    /// </summary>
    class FileValueConverter
    {
        class Table
        {
            public required string ModuleName { get; init; }
            public Dictionary<string, string> ToExternal { get; } = new();
            public Dictionary<string, string> ToInternal { get; } = new();
        }

        readonly Dictionary<string, Table> _byTarget = new();

        public bool IsEmpty => _byTarget.Count == 0;

        public static async Task<FileValueConverter> LoadAsync(ModuleDesign module,
            Func<SearchCondition, Task<List<List<string>>>> getTableTexts)
        {
            var converter = new FileValueConverter();
            foreach (var design in module.Fields.OfType<FileValueConversionFieldDesign>())
            {
                //同じ対象の重複はデザインチェックの指摘対象。実行時は先に定義された方を使う
                if (string.IsNullOrEmpty(design.TargetField) || converter._byTarget.ContainsKey(design.TargetField)) continue;
                var moduleName = design.ResolveConversionModule(module);
                var internalField = design.ResolveInternalField(module);
                if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(design.ExternalField) || string.IsNullOrEmpty(internalField)) continue;

                var texts = await getTableTexts(new SearchCondition { ModuleName = moduleName });
                var header = texts.Count == 0 ? new() : texts[0];
                var extIndex = FindColumn(header, design.ExternalField);
                var intIndex = FindColumn(header, internalField);

                var table = new Table { ModuleName = moduleName };
                if (0 <= extIndex && 0 <= intIndex)
                {
                    foreach (var row in texts.Skip(1))
                    {
                        if (row.Count <= extIndex || row.Count <= intIndex) continue;
                        table.ToExternal.TryAdd(row[intIndex], row[extIndex]);
                        table.ToInternal.TryAdd(row[extIndex], row[intIndex]);
                    }
                }
                converter._byTarget[design.TargetField] = table;
            }
            return converter;
        }

        //フィールド名だけ ("EdiCode") でも内部名ヘッダ ("EdiCode.Value") でも受け付ける
        static int FindColumn(List<string> header, string fieldName)
        {
            var exact = header.IndexOf(fieldName);
            if (0 <= exact) return exact;
            return header.FindIndex(h => h.StartsWith(fieldName + ".", StringComparison.Ordinal));
        }

        /// <summary>フィールドが変換対象か。</summary>
        public bool Converts(string fieldName) => _byTarget.ContainsKey(fieldName);

        /// <summary>変換対象フィールドの変換表モジュール名 (エラーメッセージ用)。</summary>
        public string ModuleNameOf(string fieldName) => _byTarget.TryGetValue(fieldName, out var t) ? t.ModuleName : string.Empty;

        public bool TryToExternal(string fieldName, string value, out string converted)
            => TryConvert(fieldName, value, t => t.ToExternal, out converted);

        public bool TryToInternal(string fieldName, string value, out string converted)
            => TryConvert(fieldName, value, t => t.ToInternal, out converted);

        bool TryConvert(string fieldName, string value, Func<Table, Dictionary<string, string>> selector, out string converted)
        {
            converted = value;
            if (!_byTarget.TryGetValue(fieldName, out var table)) return false;
            if (!selector(table).TryGetValue(value, out var v)) return false;
            converted = v;
            return true;
        }
    }
}
