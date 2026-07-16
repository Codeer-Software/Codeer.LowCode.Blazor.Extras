using Codeer.LowCode.Blazor.Extras.Csv;
using Codeer.LowCode.Blazor.Extras.Designs;
using Excel.Report.PDF;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Csv
{
    /// <summary>
    /// 一覧の一括ダウンロード/一括更新の CSV 対応 (Excel.Report.PDF の ExcelUtils と対になる)。
    /// テンプレートから移譲される BulkFileTransfer が使う。単体でも利用可。
    /// </summary>
    public static class CsvUtils
    {
        static CsvUtils()
            => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); //Shift_JIS 用

        /// <summary>テーブルテキストから CSV バイナリを作る (RFC 4180 準拠、改行は CRLF)。</summary>
        public static MemoryStream CreateCsvBinary(List<List<string>> allTexts, CsvEncodingKind encodingKind, char delimiter = ',')
        {
            var ms = new MemoryStream();
            //StreamWriter がエンコーディングのプリアンブル (UTF-8 BOM 等) を先頭に書く
            using (var writer = new StreamWriter(ms, GetEncoding(encodingKind), leaveOpen: true))
            {
                foreach (var row in allTexts)
                {
                    writer.Write(string.Join(delimiter, row.Select(e => CsvTextParser.Escape(e, delimiter))));
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
        public static async Task<List<List<string>>> ReadAllTextsFromFileBinary(Stream stream, CsvEncodingKind encodingKind, char delimiter = ',')
        {
            var ms = await BufferAsync(stream);
            if (IsExcel(ms)) return await ExcelUtils.ReadAllTextsFromExcelBinary(ms);
            return ReadAllTextsFromCsv(ms, encodingKind, delimiter);
        }

        /// <summary>CSV としてテーブルテキストを読む (Excel 判定なし)。</summary>
        public static List<List<string>> ReadAllTextsFromCsv(Stream buffered, CsvEncodingKind encodingKind, char delimiter = ',')
        {
            //BOM があればそちらを優先して自動判別
            using var reader = new StreamReader(buffered, GetEncoding(encodingKind), detectEncodingFromByteOrderMarks: true);
            return CsvTextParser.Parse(reader, delimiter);
        }

        /// <summary>先読みできないストリームをバッファする。</summary>
        public static async Task<MemoryStream> BufferAsync(Stream stream)
        {
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        /// <summary>xlsx (ZIP = PK ヘッダ) かどうか。位置は先頭に戻す。</summary>
        public static bool IsExcel(MemoryStream buffered)
        {
            if (buffered.Length < 2) return false;
            var head = new byte[2];
            _ = buffered.Read(head, 0, 2);
            buffered.Position = 0;
            return head[0] == 'P' && head[1] == 'K';
        }

        static Encoding GetEncoding(CsvEncodingKind kind) => kind switch
        {
            CsvEncodingKind.Utf8 => new UTF8Encoding(false),
            CsvEncodingKind.ShiftJis => Encoding.GetEncoding("shift_jis"),
            _ => new UTF8Encoding(true),
        };
    }
}
