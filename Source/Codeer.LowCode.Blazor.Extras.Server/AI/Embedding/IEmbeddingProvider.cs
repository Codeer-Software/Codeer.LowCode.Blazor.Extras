namespace Codeer.LowCode.Blazor.Extras.Server.AI.Embedding
{
    /// <summary>
    /// 文章をベクトルにする (埋め込み) プロバイダ。SemanticSearchField の索引付け (SemanticSearchIndexer) と意味検索 (RawDataAccessAgent の search_records / {embed:…}) が使う。
    /// メール送信 (IMailSender) と同じ作り: プロバイダごとの実装 (Azure OpenAI / OpenAI / Ollama) がそれぞれ自分の設定だけを受け取り、
    /// 差はこのインターフェースが吸収する (製品側にプロバイダ共通の設定型は無い)。独自実装 (ローカルモデル・社内 API 等) はテンプレートの対応表 (EmbeddingProviderTable) に足す。
    /// 索引と検索は同じモデルで作ったベクトルでないと比較できないので、プロバイダの選択はアプリ単位 (appsettings) で行う。モデルを変えたら列を作り直して全行再索引。
    /// </summary>
    public interface IEmbeddingProvider
    {
        /// <summary>モデルの識別 (デプロイ名・モデル名)。索引を作ったモデルの記録や表示に使う。</summary>
        string ModelId { get; }

        /// <summary>ベクトルの次元数。DB のベクトル列 (vector(n) / VECTOR(n)) の n と一致させる。分からなければ 0 (最初の埋め込みで分かる)。</summary>
        int Dimensions { get; }

        /// <summary>
        /// 文章をまとめてベクトルにする。戻り値は texts と同じ順・同じ数。1 回の API 呼び出しに載せられる上限を超える分は実装側で分けて呼ぶ。
        /// 失敗は例外 (呼び出し側が「保存は止めず null で保存」か「エラーにする」かを決める)。
        /// </summary>
        Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    }
}
