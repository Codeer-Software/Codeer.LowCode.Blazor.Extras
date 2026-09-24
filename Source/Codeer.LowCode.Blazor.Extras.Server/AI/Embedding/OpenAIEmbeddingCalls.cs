using OpenAI.Embeddings;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>OpenAI 系の EmbeddingClient (Azure OpenAI) を IEmbeddingProvider の形で呼ぶ部分。</summary>
    internal static class OpenAIEmbeddingCalls
    {
        /// <summary>1 リクエストに載せられる入力数の上限 (API の制限は 2048)。</summary>
        public const int MaxInputsPerRequest = 2048;

        public static async Task<IReadOnlyList<float[]>> EmbedAsync(EmbeddingClient client, int dimensions, IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            var result = new float[texts.Count][];
            var options = dimensions > 0 ? new EmbeddingGenerationOptions { Dimensions = dimensions } : null;
            for (var offset = 0; offset < texts.Count; offset += MaxInputsPerRequest)
            {
                var chunk = texts.Skip(offset).Take(MaxInputsPerRequest).ToList();
                var response = await client.GenerateEmbeddingsAsync(chunk, options, cancellationToken);
                var embeddings = response.Value;
                if (embeddings.Count != chunk.Count)
                    throw new InvalidOperationException($"The embedding model returned {embeddings.Count} vectors for {chunk.Count} texts.");
                //応答は Index で対応づける (順序が保証されないことがある)
                foreach (var embedding in embeddings)
                    result[offset + embedding.Index] = embedding.ToFloats().ToArray();
            }
            return result;
        }
    }
}
