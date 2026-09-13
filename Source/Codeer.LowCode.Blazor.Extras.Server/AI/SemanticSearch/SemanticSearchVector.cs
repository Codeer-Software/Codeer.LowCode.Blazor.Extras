using System.Runtime.InteropServices;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>埋め込みベクトルの保存形式 (float32 little-endian の並びを base64) と類似度。</summary>
    internal static class SemanticSearchVector
    {
        public static string Encode(ReadOnlySpan<float> vector)
            => Convert.ToBase64String(MemoryMarshal.AsBytes(vector));

        public static float[]? Decode(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                return bytes.Length % sizeof(float) == 0 && bytes.Length > 0 ? MemoryMarshal.Cast<byte, float>(bytes).ToArray() : null;
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
