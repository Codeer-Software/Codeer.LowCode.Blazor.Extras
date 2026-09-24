namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>
    /// Ollama (ローカル / 社内サーバーで動かすモデル) の埋め込みの設定。appsettings のセクション名はアプリが決める (テンプレートの既定は "OllamaEmbedding")。
    /// 文章が外部に出ないので、AI プロバイダに送りたくないデータの索引に使える。
    /// </summary>
    public class OllamaEmbeddingSettings
    {
        /// <summary>Ollama の URL。</summary>
        public string BaseUrl { get; set; } = "http://localhost:11434";

        /// <summary>モデル名 (bge-m3 / nomic-embed-text など。ollama pull 済みのもの)。</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>次元数 (bge-m3 は 1024、nomic-embed-text は 768)。DB の列定義と一致させる。0 なら未確認 (最初の埋め込みの長さ)。</summary>
        public int Dimensions { get; set; }

        /// <summary>1 回のリクエストに載せる文章の数。</summary>
        public int BatchSize { get; set; } = 32;
    }
}
