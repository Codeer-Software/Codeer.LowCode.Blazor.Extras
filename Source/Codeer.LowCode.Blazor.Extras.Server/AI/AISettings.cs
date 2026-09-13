namespace Codeer.LowCode.Blazor.Extras.Server.AI
{
    public class AISettings
    {
        public string OpenAIEndPoint { get; set; } = string.Empty;
        public string OpenAIKey { get; set; } = string.Empty;
        public string ChatModel { get; set; } = string.Empty;
        /// <summary>埋め込みモデルのデプロイ名 (SemanticSearchField の索引と意味検索に使う。空なら意味検索なし)。</summary>
        public string EmbeddingModel { get; set; } = string.Empty;
        public string DocumentAnalysisEndPoint { get; set; } = string.Empty;
        public string DocumentAnalysisKey { get; set; } = string.Empty;
    }
}
