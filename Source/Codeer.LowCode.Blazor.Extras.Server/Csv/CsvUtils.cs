using Codeer.LowCode.Blazor.Extras.Designs;
using Excel.Report.PDF;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Csv
{
    /// <summary>
    /// 一覧の一括ダウンロード/一括更新の CSV 対応 (Excel.Report.PDF の ExcelUtils と対になる)。
    /// モジュールに <see cref="CsvFileTransferFieldDesign"/> が定義されている場合に
    /// テンプレートの list_file / submit_by_file から使う。
    /// </summary>
    public static class CsvUtils
    {
        static CsvUtils()
            => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); //Shift_JIS 用

        /// <summary>テーブルテキストから CSV バイナリを作る (RFC 4180 準拠、改行は CRLF)。</summary>
        public static MemoryStream CreateCsvBinary(List<List<string>> allTexts, CsvFileTransferFieldDesign.CsvEncodingKind encodingKind)
        {
            var ms = new MemoryStream();
            //StreamWriter がエンコーディングのプリアンブル (UTF-8 BOM 等) を先頭に書く
            using (var writer = new StreamWriter(ms, GetEncoding(encodingKind), leaveOpen: true))
            {
                foreach (var row in allTexts)
                {
                    writer.Write(string.Join(",", row.Select(Escape)));
                    writer.Write("\r\n");
                }
            }
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// アップロードされたファイルからテーブルテキストを読む。
        /// 内容で判定し、xlsx (ZIP = PK ヘッダ) なら Excel、それ以外は CSV としてパースする。
        /// </summary>
        public static async Task<List<List<string>>> ReadAllTextsFromFileBinary(Stream stream, CsvFileTransferFieldDesign.CsvEncodingKind encodingKind)
        {
            //Request.Body は先読みできないので一度バッファする
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            //xlsx は ZIP (PK) で始まる
            if (2 <= ms.Length)
            {
                var head = new byte[2];
                _ = ms.Read(head, 0, 2);
                ms.Position = 0;
                if (head[0] == 'P' && head[1] == 'K') return await ExcelUtils.ReadAllTextsFromExcelBinary(ms);
            }

            //BOM があればそちらを優先して自動判別
            using var reader = new StreamReader(ms, GetEncoding(encodingKind), detectEncodingFromByteOrderMarks: true);
            return ParseCsv(reader);
        }

        //RFC 4180: カンマ・引用符・改行を含むフィールドは引用符で囲み、引用符は二重にする
        static string Escape(string? text)
        {
            text ??= string.Empty;
            if (text.IndexOfAny([',', '"', '\r', '\n']) < 0) return text;
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        //RFC 4180 準拠のパース (引用符内のカンマ・改行・二重引用符に対応)。全セル空の行は除外する
        static List<List<string>> ParseCsv(TextReader reader)
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
                switch (ch)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
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

        static void AddRow(List<List<string>> rows, List<string> row)
        {
            if (row.All(string.IsNullOrEmpty)) return;
            rows.Add(row);
        }

        static Encoding GetEncoding(CsvFileTransferFieldDesign.CsvEncodingKind kind) => kind switch
        {
            CsvFileTransferFieldDesign.CsvEncodingKind.Utf8 => new UTF8Encoding(false),
            CsvFileTransferFieldDesign.CsvEncodingKind.ShiftJis => Encoding.GetEncoding("shift_jis"),
            _ => new UTF8Encoding(true),
        };
    }
}
