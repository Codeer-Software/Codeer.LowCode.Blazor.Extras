using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Csv
{
    /// <summary>
    /// RFC 4180 準拠の CSV/TSV テキストパーサ (引用符内の区切り・改行・二重引用符に対応)。
    /// サーバーの CsvUtils (一括ダウンロード/更新) と LocalizeService (TSV リソース) で共用する。
    /// 不正な入力 (引用符なしセル内の裸の引用符など) はエラーにせず寛容に読む。
    /// </summary>
    public static class CsvTextParser
    {
        /// <summary>パースする (引用符内の区切り・改行・二重引用符に対応)。全セル空の行は除外する。</summary>
        public static List<List<string>> Parse(TextReader reader, char delimiter)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;

            int c;
            while ((c = reader.Read()) != -1)
            {
                var ch = (char)c;
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (reader.Peek() == '"')
                        {
                            field.Append('"');
                            reader.Read();
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(ch);
                    }
                    continue;
                }
                if (ch == delimiter)
                {
                    row.Add(field.ToString());
                    field.Clear();
                    continue;
                }
                switch (ch)
                {
                    case '"': inQuotes = true; break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        AddRow(rows, row); row = new();
                        break;
                    default: field.Append(ch); break;
                }
            }
            if (0 < field.Length || 0 < row.Count)
            {
                row.Add(field.ToString());
                AddRow(rows, row);
            }
            return rows;
        }

        /// <summary>出力用エスケープ (RFC 4180)。区切り・引用符・改行を含むフィールドは引用符で囲み、引用符は二重にする。</summary>
        public static string Escape(string? text, char delimiter)
        {
            text ??= string.Empty;
            if (text.IndexOfAny([delimiter, '"', '\r', '\n']) < 0) return text;
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        static void AddRow(List<List<string>> rows, List<string> row)
        {
            if (row.All(string.IsNullOrEmpty)) return;
            rows.Add(row);
        }
    }
}
