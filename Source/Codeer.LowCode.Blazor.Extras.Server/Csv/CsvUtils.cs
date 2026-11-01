using Codeer.LowCode.Blazor.Extras.Csv;
using Codeer.LowCode.Blazor.Extras.Designs;
using Excel.Report.PDF;
using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Csv
{
    /// <summary>
    /// 一覧の一括ダウンロード/一括更新の CSV 対応 (Excel.Report.PDF の ExcelUtils と対になる)。
    /// テンプレートから移譲される BulkFileTransfer が使う。単体でも利用可。
    /// </summary>
    /// <remarks>
    /// 式インジェクション対策 (preventFormulaInjection = CsvFileFormatFieldDesign.PreventFormulaInjection。既定 false):
    /// Excel 等の表計算ソフトが CSV を開くとき数式として評価する先頭文字 (= + - @ タブ CR) のセルは、
    /// 出力時に先頭へ <c>'</c> を付ける (負の数など数値は付けない)。取込時はセル先頭の <c>'</c> を 1 つ外す。
    /// 値自体が <c>'</c> で始まるセルは出力時に <c>'</c> を 1 つ足して往復で元に戻す (Excel は CSV の先頭の <c>'</c> を文字として保持する)。
    /// xlsx の出力は文字列セルとして書くので対象外。
    /// </remarks>
    public static class CsvUtils
    {
        static CsvUtils()
            => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); //Shift_JIS 用

        /// <summary>テーブルテキストから CSV バイナリを作る (RFC 4180 準拠、改行は CRLF)。preventFormulaInjection は式インジェクション対策 (クラスの説明)。</summary>
        public static MemoryStream CreateCsvBinary(List<List<string>> allTexts, CsvEncodingKind encodingKind, char delimiter = ',', bool preventFormulaInjection = false)
        {
            var ms = new MemoryStream();
            //StreamWriter がエンコーディングのプリアンブル (UTF-8 BOM 等) を先頭に書く
            using (var writer = new StreamWriter(ms, GetEncoding(encodingKind), leaveOpen: true))
            {
                foreach (var row in allTexts)
                {
                    writer.Write(string.Join(delimiter, row.Select(e => CsvTextParser.Escape(preventFormulaInjection ? GuardFormula(e) : e, delimiter))));
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
        public static async Task<List<List<string>>> ReadAllTextsFromFileBinary(Stream stream, CsvEncodingKind encodingKind, char delimiter = ',', bool preventFormulaInjection = false)
        {
            var ms = await BufferAsync(stream);
            if (IsExcel(ms))
            {
                //xlsx はダウンロードした CSV を Excel で保存し直したもの (' は文字として残っている) なので、同じく先頭の ' を外す
                var rows = await ExcelUtils.ReadAllTextsFromExcelBinary(ms);
                return preventFormulaInjection ? UnguardFormula(rows) : rows;
            }
            return ReadAllTextsFromCsv(ms, encodingKind, delimiter, preventFormulaInjection);
        }

        /// <summary>CSV としてテーブルテキストを読む (Excel 判定なし)。</summary>
        public static List<List<string>> ReadAllTextsFromCsv(Stream buffered, CsvEncodingKind encodingKind, char delimiter = ',', bool preventFormulaInjection = false)
        {
            //BOM があればそちらを優先して自動判別
            using var reader = new StreamReader(buffered, GetEncoding(encodingKind), detectEncodingFromByteOrderMarks: true);
            var rows = CsvTextParser.Parse(reader, delimiter);
            return preventFormulaInjection ? UnguardFormula(rows) : rows;
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

        //エンコーディング種別の実効値 (固定長形式 (FixedLengthUtils) とも共用)
        internal static Encoding GetEncoding(CsvEncodingKind kind) => kind switch
        {
            CsvEncodingKind.Utf8 => new UTF8Encoding(false),
            CsvEncodingKind.ShiftJis => Encoding.GetEncoding("shift_jis"),
            _ => new UTF8Encoding(true),
        };

        //出力: 表計算ソフトが数式として評価しうるセルと、' で始まるセル (往復で戻すため) に ' を前置する
        static string? GuardFormula(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            switch (text[0])
            {
                case '\'':
                case '=':
                case '@':
                case '\t':
                case '\r':
                    return "'" + text;
                case '+':
                case '-':
                    //"-123" や "+1.5" は数値のまま。"-" 1 文字も数式にならない。"-2+3" "+cmd|..." は式
                    if (text.Length == 1 || double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return text;
                    return "'" + text;
                default:
                    return text;
            }
        }

        //取込: セル先頭の ' を 1 つ外す (出力で付けたもの。Excel は CSV の先頭の ' を文字として保持するので再保存後も残っている)
        static List<List<string>> UnguardFormula(List<List<string>> rows)
        {
            foreach (var row in rows)
            {
                for (var i = 0; i < row.Count; i++)
                {
                    if (row[i].StartsWith('\'')) row[i] = row[i][1..];
                }
            }
            return rows;
        }
    }
}
