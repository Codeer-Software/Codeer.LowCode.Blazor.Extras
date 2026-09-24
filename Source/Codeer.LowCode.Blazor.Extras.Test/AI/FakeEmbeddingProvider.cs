using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// テスト用の埋め込みプロバイダ。文字の 2-gram をハッシュで 128 次元に畳んで正規化する (決定的・外部呼び出しなし)。
    /// 同じ語を含む文どうしが近くなるので、意味検索の並び順の検証に使える。<see cref="Fail"/> で失敗を再現できる。
    /// </summary>
    public sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public const int Dimensions = 128;
        public List<string> Inputs { get; } = new();
        /// <summary>EmbedAsync の呼び出し回数 (まとめて埋め込んでいるかの確認用)。</summary>
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        /// <summary>1 回の呼び出しにかかる時間 (中断のテスト用)。</summary>
        public TimeSpan Delay { get; set; }

        public string ModelId => "fake-2gram-128";

        int IEmbeddingProvider.Dimensions => Dimensions;

        public static float[] Embed(string text)
        {
            var v = new float[Dimensions];
            var s = text.Replace("\r", "").Replace("\n", " ");
            for (var i = 0; i + 1 < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]) || char.IsWhiteSpace(s[i + 1])) continue;
                var h = (s[i] * 31 + s[i + 1]) & 0x7fffffff;
                v[h % Dimensions] += 1;
            }
            var norm = Math.Sqrt(v.Sum(x => (double)x * x));
            if (norm > 0) for (var i = 0; i < v.Length; i++) v[i] = (float)(v[i] / norm);
            return v;
        }

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("embedding failed (fake)");
            Calls++;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            Inputs.AddRange(texts);
            return texts.Select(Embed).ToList();
        }
    }
}
