namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>
    /// OpenAI (api.openai.com) の埋め込みモデルの設定。appsettings のセクション名はアプリが決める (テンプレートの既定は "OpenAIEmbedding")。
    /// </summary>
    public class OpenAIEmbeddingSettings
    {
        /// <summary>API キー。appsettings.Development.json / 環境変数 (OpenAIEmbedding__Key) に置く。</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>モデル名 (text-embedding-3-small など)。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>次元数。0 ならモデルの既定。text-embedding-3 系は指定した次元に縮めて返せる。</summary>
        public int Dimensions { get; set; }
    }
}
