using Azure;
using Azure.AI.OpenAI;
using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Extras.Server.Services;
using Microsoft.Extensions.AI;

namespace Extras.Server.AI
{
    /// <summary>
    /// SemanticSearchField (意味検索) のサーバー側の持ち物。
    /// 埋め込みモデル (IEmbeddingGenerator) の作り方はアプリの責務で、ここでは AISettings の Azure OpenAI (EmbeddingModel) を使う。
    /// <see cref="Indexer"/> は CustomizedModuleDataIO が保存時に呼び (文章にベクトルを付ける)、
    /// <see cref="EmbeddingGeneratorFactory"/> は AIChatAgentTable が RawDataAccessAgent に渡す (search_records で似た記録を探す)。
    /// EmbeddingModel が空なら埋め込み無し = 文章だけ保存され、意味検索ツールは付かない。
    /// </summary>
    internal static class SemanticSearchIndex
    {
        static readonly Lazy<Func<IEmbeddingGenerator<string, Embedding<float>>>?> _factory = new(() => CreateAzureOpenAI(SystemConfig.Instance.AISettings));

        /// <summary>埋め込みモデルの取り方 (未設定なら null)。</summary>
        public static Func<IEmbeddingGenerator<string, Embedding<float>>>? EmbeddingGeneratorFactory => _factory.Value;

        /// <summary>保存時に索引を付けるヘルパー (プロセスに 1 つ)。</summary>
        public static SemanticSearchIndexer Indexer { get; } = new(() => EmbeddingGeneratorFactory?.Invoke());

        static Func<IEmbeddingGenerator<string, Embedding<float>>>? CreateAzureOpenAI(AISettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OpenAIEndPoint) || string.IsNullOrWhiteSpace(settings.OpenAIKey) || string.IsNullOrWhiteSpace(settings.EmbeddingModel))
                return null;
            var client = new AzureOpenAIClient(new Uri(settings.OpenAIEndPoint), new AzureKeyCredential(settings.OpenAIKey));
            var generator = client.GetEmbeddingClient(settings.EmbeddingModel).AsIEmbeddingGenerator();
            return () => generator;
        }
    }
}
