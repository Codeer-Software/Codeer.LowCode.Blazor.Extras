using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Extras.Server.Services;

namespace Extras.Server.AI
{
    /// <summary>
    /// SemanticSearchField (意味検索) のサーバー側の持ち物。
    /// 埋め込みプロバイダは appsettings の SemanticSearch.EmbeddingProvider の呼び名で <see cref="EmbeddingProviderTable"/> から選ぶ (Azure OpenAI / 独自)。
    /// <see cref="Indexer"/> は CustomizedModuleDataIO が保存時に呼び (文章にベクトルを付ける)、
    /// <see cref="Provider"/> は AIChatAgentTable が RawDataAccessAgent に渡す (search_records で似た記録を探す)。
    /// <see cref="Jobs"/> は再索引 (全行の文章とベクトルの作り直し) のジョブ置き場で、SemanticSearchController の窓口から使う。
    /// 呼び名が空 (対応表に無い) なら埋め込み無し = 文章だけ保存され、意味検索ツールは付かない。
    /// </summary>
    internal static class SemanticSearchIndex
    {
        static readonly Lazy<IEmbeddingProvider?> _provider = new(() => EmbeddingProviderTable.Create(SystemConfig.Instance.SemanticSearch.EmbeddingProvider));

        /// <summary>埋め込みプロバイダ (未設定なら null)。</summary>
        public static IEmbeddingProvider? Provider => _provider.Value;

        /// <summary>保存時に索引を付けるヘルパー (プロセスに 1 つ)。</summary>
        public static SemanticSearchIndexer Indexer { get; } = new(() => Provider);

        /// <summary>再索引 (フィールドのスクリプト Reindex) のジョブ置き場。SemanticSearchController が使う (プロセスに 1 つ)。</summary>
        public static SemanticSearchReindexJobStore Jobs { get; } = new(Indexer, () => DesignerService.GetDesignData());
    }
}
