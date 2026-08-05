using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Csv;
using Excel.Report.PDF;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// 一覧の一括ダウンロード/一括更新の固定長形式対応 (CsvUtils と対になる)。
    /// <see cref="CsvFileFormatFieldDesign"/> (形式 = 幅の単位・エンコーディング) と
    /// <see cref="FileColumnMappingFieldDesign"/> (列構成 = 各列の FixedLengthWidth/Alignment/PaddingChar) の組で動作し、
    /// 形式フィールドの Delimiter が None (区切り文字なし) のとき BulkFileTransfer が使う。
    /// 出力で幅に収まらない値は黙って切り詰めず、行番号付きのエラーで失敗させる。
    /// </summary>
    internal static class FixedLengthUtils
    {
        /// <summary>
        /// テーブルテキスト (列マッピング変換済みの外部列) から固定長ファイルバイナリを作る (改行は CRLF)。
        /// 幅に収まらない値がある場合は行番号付きのメッセージで <see cref="LowCodeException"/> を投げる。
        /// </summary>
        internal static MemoryStream CreateFixedLengthBinary(List<List<string>> allTexts, FileColumnMappingFieldDesign columns, CsvFileFormatFieldDesign format)
        {
            var encoding = CsvUtils.GetEncoding(format.Encoding);
            var cols = columns.Columns.Items;
            var errors = new List<string>();

            var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, encoding, leaveOpen: true))
            {
                var rowNo = 0;
                foreach (var row in allTexts)
                {
                    rowNo++;
                    var line = new StringBuilder();
                    for (var i = 0; i < cols.Count; i++)
                    {
                        var c = cols[i];
                        var value = i < row.Count ? row[i] : string.Empty;
                        var length = GetLength(value, format.FixedLengthWidthUnit, encoding);
                        if (c.FixedLengthWidth < length)
                        {
                            errors.Add($"Row {rowNo}, {ColumnLabel(c)}: '{value}' exceeds the width {c.FixedLengthWidth}.");
                            continue;
                        }
                        var padding = new string(c.FixedLengthPaddingChar == FixedLengthPaddingCharKind.Zero ? '0' : ' ', c.FixedLengthWidth - length);
                        line.Append(c.FixedLengthAlignment == FixedLengthAlignmentKind.Right ? padding + value : value + padding);
                    }
                    writer.Write(line);
                    writer.Write("\r\n");
                }
            }
            if (errors.Count != 0) throw new LowCodeException(string.Join(Environment.NewLine, Cap(errors)));

            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// アップロードされたファイルからテーブルテキストを読む。
        /// 内容で判定し、xlsx (ZIP = PK ヘッダ) なら Excel、それ以外は固定長としてパースする
        /// (CSV 経路と同じく、ダウンロードしたファイルを Excel で編集して戻す運用も受け付ける)。
        /// </summary>
        internal static async Task<List<List<string>>> ReadAllTextsFromFileBinary(Stream stream, FileColumnMappingFieldDesign columns, CsvFileFormatFieldDesign format)
        {
            var ms = await CsvUtils.BufferAsync(stream);
            if (CsvUtils.IsExcel(ms)) return await ExcelUtils.ReadAllTextsFromExcelBinary(ms);
            return ReadAllTextsFromFixedLength(ms, columns, format);
        }

        /// <summary>
        /// 固定長としてテーブルテキストを読む (Excel 判定なし)。
        /// 行が全列分に満たない場合、足りない列は空文字にする (末尾の空白がトリムされたファイルへの寛容さ)。
        /// 各列はパディング側 (寄せの逆側) からパディング文字を取り除く。
        /// </summary>
        internal static List<List<string>> ReadAllTextsFromFixedLength(MemoryStream buffered, FileColumnMappingFieldDesign columns, CsvFileFormatFieldDesign format)
        {
            var encoding = CsvUtils.GetEncoding(format.Encoding);
            var cols = columns.Columns.Items;
            var bytes = buffered.ToArray();

            //BOM は読み飛ばす (Utf8 設定でも BOM 付きファイルを受け付ける)
            var preamble = new UTF8Encoding(true).GetPreamble();
            var offset = preamble.Length <= bytes.Length && preamble.AsSpan().SequenceEqual(bytes.AsSpan(0, preamble.Length))
                ? preamble.Length : 0;

            var result = new List<List<string>>();
            foreach (var (start, length) in SplitLines(bytes, offset))
            {
                if (length == 0) continue;
                var row = new List<string>();
                if (format.FixedLengthWidthUnit == FixedLengthWidthUnitKind.Byte)
                {
                    var pos = start;
                    var end = start + length;
                    foreach (var c in cols)
                    {
                        var take = Math.Min(c.FixedLengthWidth, Math.Max(0, end - pos));
                        row.Add(Unpad(take <= 0 ? string.Empty : encoding.GetString(bytes, pos, take), c));
                        pos += c.FixedLengthWidth;
                    }
                }
                else
                {
                    var line = encoding.GetString(bytes, start, length);
                    var pos = 0;
                    foreach (var c in cols)
                    {
                        var take = Math.Min(c.FixedLengthWidth, Math.Max(0, line.Length - pos));
                        row.Add(Unpad(take <= 0 ? string.Empty : line.Substring(pos, take), c));
                        pos += c.FixedLengthWidth;
                    }
                }
                result.Add(row);
            }
            return result;
        }

        static int GetLength(string value, FixedLengthWidthUnitKind unit, Encoding encoding)
            => unit == FixedLengthWidthUnitKind.Byte ? encoding.GetByteCount(value) : value.Length;

        static string Unpad(string cell, MappingColumn c)
        {
            var pad = c.FixedLengthPaddingChar == FixedLengthPaddingCharKind.Zero ? '0' : ' ';
            if (c.FixedLengthAlignment == FixedLengthAlignmentKind.Right)
            {
                var value = cell.TrimStart(pad);
                //ゼロ埋めの全ゼロは空ではなく値 0
                if (value.Length == 0 && cell.Length != 0 && pad == '0') return "0";
                return value;
            }
            return cell.TrimEnd(pad);
        }

        //改行 (CRLF / LF) の位置で行に分割する。CsvEncodingKind の各エンコーディングは
        //マルチバイト列に 0x0A が現れないためバイトのまま分割できる
        static IEnumerable<(int Start, int Length)> SplitLines(byte[] bytes, int offset)
        {
            var start = offset;
            for (var i = offset; i < bytes.Length; i++)
            {
                if (bytes[i] != '\n') continue;
                var end = i;
                if (start < end && bytes[end - 1] == '\r') end--;
                yield return (start, end - start);
                start = i + 1;
            }
            if (start < bytes.Length) yield return (start, bytes.Length - start);
        }

        static string ColumnLabel(MappingColumn c) => string.IsNullOrEmpty(c.ExternalName) ? c.Field : c.ExternalName;

        static IEnumerable<string> Cap(List<string> errors)
        {
            const int max = 20;
            foreach (var e in errors.Take(max)) yield return e;
            if (max < errors.Count) yield return $"...and {errors.Count - max} more errors.";
        }
    }
}
