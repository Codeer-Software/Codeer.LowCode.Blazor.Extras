using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.ClientModel;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>Azure OpenAI の埋め込みモデルで <see cref="IEmbeddingProvider"/> を実装する。クライアントは 1 つ作って使い回す。</summary>
    public sealed class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
    {
        readonly EmbeddingClient _client;
        readonly int _dimensions;

        public AzureOpenAIEmbeddingProvider(AzureOpenAIEmbeddingSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.EndPoint) || string.IsNullOrWhiteSpace(settings.Key) || string.IsNullOrWhiteSpace(settings.Deployment))
                throw new InvalidOperationException("AzureOpenAIEmbeddingSettings requires EndPoint, Key and Deployment.");
            _client = new AzureOpenAIClient(new Uri(settings.EndPoint), new ApiKeyCredential(settings.Key)).GetEmbeddingClient(settings.Deployment);
            _dimensions = settings.Dimensions;
            ModelId = settings.Deployment;
        }

        public string ModelId { get; }

        public int Dimensions => _dimensions;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            => OpenAIEmbeddingCalls.EmbedAsync(_client, _dimensions, texts, cancellationToken);
    }
}
