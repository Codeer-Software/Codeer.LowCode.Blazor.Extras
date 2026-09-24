using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>
    /// Microsoft.Extensions.AI の <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> (OllamaSharp・ONNX 等の既存実装) を <see cref="IEmbeddingProvider"/> として使うアダプタ。
    /// 手元にその型の実装があるときの受け口で、製品の経路は IEmbeddingProvider だけを見る。
    /// </summary>
    public sealed class EmbeddingGeneratorProvider : IEmbeddingProvider
    {
        readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

        /// <param name="modelId">モデルの識別 (空なら生成器のメタデータから取る)</param>
        /// <param name="dimensions">次元数 (0 なら生成器のメタデータから取る。無ければ 0)</param>
        public EmbeddingGeneratorProvider(IEmbeddingGenerator<string, Embedding<float>> generator, string modelId = "", int dimensions = 0)
        {
            _generator = generator;
            var metadata = generator.GetService<EmbeddingGeneratorMetadata>();
            ModelId = !string.IsNullOrEmpty(modelId) ? modelId : metadata?.DefaultModelId ?? generator.GetType().Name;
            Dimensions = dimensions > 0 ? dimensions : metadata?.DefaultModelDimensions ?? 0;
        }

        public string ModelId { get; }

        public int Dimensions { get; }

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var embeddings = await _generator.GenerateAsync(texts, cancellationToken: cancellationToken);
            if (embeddings.Count != texts.Count) throw new InvalidOperationException($"The embedding generator returned {embeddings.Count} vectors for {texts.Count} texts.");
            return embeddings.Select(e => e.Vector.ToArray()).ToList();
        }
    }
}
