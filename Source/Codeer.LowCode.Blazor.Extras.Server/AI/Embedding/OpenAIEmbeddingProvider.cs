using OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>OpenAI (api.openai.com) の埋め込みモデルで <see cref="IEmbeddingProvider"/> を実装する。</summary>
    public sealed class OpenAIEmbeddingProvider : IEmbeddingProvider
    {
        readonly EmbeddingClient _client;
        readonly int _dimensions;

        public OpenAIEmbeddingProvider(OpenAIEmbeddingSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Key) || string.IsNullOrWhiteSpace(settings.Model))
                throw new InvalidOperationException("OpenAIEmbeddingSettings requires Key and Model.");
            _client = new OpenAIClient(new ApiKeyCredential(settings.Key)).GetEmbeddingClient(settings.Model);
            _dimensions = settings.Dimensions;
            ModelId = settings.Model;
        }

        public string ModelId { get; }

        public int Dimensions => _dimensions;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            => OpenAIEmbeddingCalls.EmbedAsync(_client, _dimensions, texts, cancellationToken);
    }
}
