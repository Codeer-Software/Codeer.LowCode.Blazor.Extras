using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>
    /// Ollama の埋め込み API (POST {BaseUrl}/api/embed) で <see cref="IEmbeddingProvider"/> を実装する。SDK は使わず HttpClient で直接呼ぶ (依存を増やさない)。
    /// 文章はサーバーの外に出ない (Ollama が動いている場所まで)。
    /// </summary>
    public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        static readonly HttpClient _sharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

        readonly HttpClient _http;
        readonly Uri _endpoint;
        readonly OllamaEmbeddingSettings _settings;

        /// <param name="http">テストや独自の設定 (プロキシ等) で HttpClient を差し替えるとき。null なら共有のものを使う</param>
        public OllamaEmbeddingProvider(OllamaEmbeddingSettings settings, HttpClient? http = null)
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.Model))
                throw new InvalidOperationException("OllamaEmbeddingSettings requires BaseUrl and Model.");
            _settings = settings;
            _endpoint = new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), "api/embed");
            _http = http ?? _sharedHttp;
        }

        public string ModelId => _settings.Model;

        public int Dimensions => _settings.Dimensions;

        public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            var result = new float[texts.Count][];
            var batch = Math.Max(1, _settings.BatchSize);
            for (var offset = 0; offset < texts.Count; offset += batch)
            {
                var chunk = texts.Skip(offset).Take(batch).ToList();
                using var response = await _http.PostAsJsonAsync(_endpoint, new EmbedRequest { Model = _settings.Model, Input = chunk }, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new InvalidOperationException($"Ollama /api/embed failed ({(int)response.StatusCode}): {body}");
                }
                var embed = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException("Ollama /api/embed returned an empty response.");
                if (embed.Embeddings == null || embed.Embeddings.Count != chunk.Count)
                    throw new InvalidOperationException($"Ollama returned {embed.Embeddings?.Count ?? 0} vectors for {chunk.Count} texts.");
                for (var i = 0; i < chunk.Count; i++) result[offset + i] = embed.Embeddings[i];
            }
            return result;
        }

        sealed class EmbedRequest
        {
            [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
            [JsonPropertyName("input")] public List<string> Input { get; set; } = new();
        }

        sealed class EmbedResponse
        {
            [JsonPropertyName("model")] public string? Model { get; set; }
            [JsonPropertyName("embeddings")] public List<float[]>? Embeddings { get; set; }
        }
    }
}
