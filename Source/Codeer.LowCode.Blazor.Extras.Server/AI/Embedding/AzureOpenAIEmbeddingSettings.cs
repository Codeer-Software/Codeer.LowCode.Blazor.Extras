namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>
    /// Azure OpenAI の埋め込みモデルの設定。appsettings のセクション名はアプリが決める (テンプレートの対応表が読む。既定は "AzureOpenAIEmbedding")。
    /// チャット (AISettings) とは独立なので、チャットは Azure・埋め込みは別プロバイダ、のような組み合わせができる。
    /// </summary>
    public class AzureOpenAIEmbeddingSettings
    {
        /// <summary>リソースのエンドポイント (https://xxx.openai.azure.com/)。</summary>
        public string EndPoint { get; set; } = string.Empty;

        /// <summary>API キー。appsettings.Development.json / 環境変数 (AzureOpenAIEmbedding__Key) に置く。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>埋め込みモデルのデプロイ名 (text-embedding-3-small など)。</summary>
        public string Deployment { get; set; } = string.Empty;

        /// <summary>
        /// 次元数。0 ならモデルの既定 (text-embedding-3-small は 1536、text-embedding-3-large は 3072)。
        /// text-embedding-3 系は指定した次元に縮めて返せる (DB の列定義と一致させる)。
        /// </summary>
        public int Dimensions { get; set; }
    }
}
