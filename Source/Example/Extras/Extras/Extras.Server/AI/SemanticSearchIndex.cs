using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Extras.Server.Services;
using Microsoft.Extensions.AI;

namespace Extras.Server.AI
{
    /// <summary>
    /// SemanticSearchField (意味検索) のサーバー側の持ち物。
    /// 埋め込みモデルは AISettings の Azure OpenAI (EmbeddingModel) を Extras.Server の AzureOpenAIClients で作る (別プロバイダならここを差し替える)。
    /// <see cref="Indexer"/> は CustomizedModuleDataIO が保存時に呼び (文章にベクトルを付ける)、
    /// <see cref="EmbeddingGeneratorFactory"/> は AIChatAgentTable が RawDataAccessAgent に渡す (search_records で似た記録を探す)。
    /// <see cref="Jobs"/> は再索引 (全行の文章とベクトルの作り直し) のジョブ置き場で、SemanticSearchController の窓口から使う。
    /// EmbeddingModel が空なら埋め込み無し = 文章だけ保存され、意味検索ツールは付かない。
    /// </summary>
    internal static class SemanticSearchIndex
    {
        static readonly Lazy<Func<IEmbeddingGenerator<string, Embedding<float>>>?> _factory = new(() => AzureOpenAIClients.EmbeddingGeneratorFactory(SystemConfig.Instance.AISettings));

        /// <summary>埋め込みモデルの取り方 (未設定なら null)。</summary>
        public static Func<IEmbeddingGenerator<string, Embedding<float>>>? EmbeddingGeneratorFactory => _factory.Value;

        /// <summary>保存時に索引を付けるヘルパー (プロセスに 1 つ)。</summary>
        public static SemanticSearchIndexer Indexer { get; } = new(() => EmbeddingGeneratorFactory?.Invoke());

        /// <summary>再索引 (フィールドのスクリプト Reindex) のジョブ置き場。SemanticSearchController が使う (プロセスに 1 つ)。</summary>
        public static SemanticSearchReindexJobStore Jobs { get; } = new(Indexer, () => DesignerService.GetDesignData());
    }
}
