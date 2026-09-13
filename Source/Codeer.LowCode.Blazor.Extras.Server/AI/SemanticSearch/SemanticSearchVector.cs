using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// 埋め込みベクトルの保存形式と類似度。
    /// 保存形式は JSON 配列風のテキスト <c>[0.1,-0.2,…]</c>。pgvector (PostgreSQL) / SQL Server 2025 の VECTOR 型 / MySQL 9 の VECTOR 型が
    /// この文字列からのキャストを受け付けるので、DB 側のベクトル検索に生成列や直接のキャストでそのまま使える (base64 だと DB が読めない)。
    /// 読み込みは後方互換で base64 (float32 little-endian) も受ける。
    /// </summary>
    internal static class SemanticSearchVector
    {
        public static string Encode(ReadOnlySpan<float> vector)
        {
            var sb = new StringBuilder(vector.Length * 10 + 2);
            sb.Append('[');
            for (var i = 0; i < vector.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        public static float[]? Decode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var s = text.Trim();
            if (s.StartsWith('[') && s.EndsWith(']'))
            {
                var parts = s[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 0) return null;
                var result = new float[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) return null;
                }
                return result;
            }
            //旧形式 (base64 の float32 列)
            try
            {
                var bytes = Convert.FromBase64String(s);
                return bytes.Length % sizeof(float) == 0 && bytes.Length > 0 ? System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes).ToArray() : null;
            }
            catch (FormatException) { return null; }
        }

        /// <summary>コサイン類似度 (-1〜1)。長さが違う・ゼロベクトルなら 0。</summary>
        public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            if (a.Length != b.Length || a.Length == 0) return 0;
            double dot = 0, na = 0, nb = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += (double)a[i] * b[i];
                na += (double)a[i] * a[i];
                nb += (double)b[i] * b[i];
            }
            return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }
    }
}
