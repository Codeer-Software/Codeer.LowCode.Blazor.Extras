namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// <see cref="RawDataAccessAgent"/> の設定 (文字列と数値だけ。appsettings のセクションからそのまま束縛できる)。
    /// DB への接続手段 (IDbAccessor の作り方)・デザイン定義・モデル・ログはこの設定ではなく依存なので、Agent のコンストラクタで渡す。
    /// <para>
    /// 権限は DB 側で制御する前提: <see cref="DataSourceNames"/> には、AI 用に作った読み取り専用の DB ユーザー
    /// (見せてよい表・列だけ SELECT を GRANT) で接続するデータソースを指定する。アプリ本体と同じ接続を渡すと、
    /// ログインユーザーの権限に関係なく DB 全体が読める。SQLite はユーザーが無いので接続文字列の <c>Mode=ReadOnly</c> で読み取り専用にする。
    /// </para>
    /// </summary>
    public class RawDataAccessOptions
    {
        /// <summary>AI が使うデータソース名 (appsettings の DataSources の Name)。複数可。1 つの SQL は 1 つのデータソースに対して実行される (データソースをまたぐ JOIN はできない)。</summary>
        public IList<string> DataSourceNames { get; set; } = new List<string>();

        /// <summary>1 回の SQL で AI に返す行数の上限 (超えた分は切り捨てて「続きあり」と伝える)。</summary>
        public int MaxRows { get; set; } = 200;

        /// <summary>1 回の SQL で AI に返す文字数の上限 (トークンの歯止め)。</summary>
        public int MaxResultChars { get; set; } = 20000;

        /// <summary>SQL 1 文のタイムアウト (秒)。</summary>
        public int CommandTimeoutSeconds { get; set; } = 30;

        /// <summary>スキーマ情報を再読込するまでの時間。</summary>
        public TimeSpan SchemaCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>スキーマ説明から外す表 (DB 側で GRANT していない表は元々見えない。ここは補助)。大文字小文字は区別しない。</summary>
        public IList<string> ExcludedTables { get; } = new List<string>();

        /// <summary>システムプロンプトに追記する業務固有の説明 (用語、よく聞かれる集計の定義など)。文書として渡すなら <see cref="AIChatDocument"/> のほうが管理しやすい。</summary>
        public string AdditionalInstructions { get; set; } = string.Empty;

        // ---- 会話 (モデルとのやりとり) の設定 ----

        /// <summary>
        /// システムプロンプト。この後にツールの説明 (設計参照・DB 参照・グラフ) と依頼したユーザー名が続く。
        /// 既定は「設計と補足文書で業務の意味を確かめてから SQL で答えるデータアナリスト」(日本語)。
        /// </summary>
        public string SystemPrompt { get; set; } =
            "あなたは業務 Web アプリケーションに組み込まれたデータアナリストです。アプリの設計 (モジュール定義) と補足文書で業務の意味を確かめ、データベースに問い合わせて、データに関する質問に答えます。" +
            "質問に出てくる用語 (売上、在庫など) は、補足文書に定義があればその定義に従い、設計の候補値 (状態コード等) で条件に落としてください。定義を確かめずに列名だけで解釈しないこと。" +
            "ユーザーの言語で、簡潔に、Markdown で答えてください。使った数字は表で示し、短い解釈を添えてください。" +
            "数字を作らないこと。すべての数値はクエリ結果に基づくものだけにしてください。データで答えられない質問には、その旨と代わりに確認できることを伝えてください。";

        /// <summary>1 回の返事で許すツール呼び出し (SQL 実行・設計参照・グラフ) の往復回数の上限 (暴走とコストの歯止め)。</summary>
        public int MaxToolCallRoundsPerReply { get; set; } = 10;

        /// <summary>会話履歴として保持するユーザー発言の数 (古いターンから捨てる。ツール呼び出しと結果は同じターンとして一緒に扱う)。</summary>
        public int MaxHistoryTurns { get; set; } = 20;

        /// <summary>
        /// ツール呼び出しと結果 (SQL の結果 JSON 等、履歴の中で最も大きい) を残す直近ターン数。それより古いターンはモデルの文章だけ残す。
        /// 直前の結果を指した追問 (「その中で一番多いのは」) に答えられる範囲を保ちつつ、トークンの膨張を抑える。
        /// </summary>
        public int KeepToolResultsForTurns { get; set; } = 2;

        /// <summary>会話履歴の文字数の上限 (トークン量の近似)。超えたら古いターンから捨てる (直近 1 ターンは残す)。0 で無制限。</summary>
        public int MaxHistoryCharacters { get; set; } = 40000;

        /// <summary>最後のアクセスからこの時間を過ぎた会話履歴は捨てる。</summary>
        public TimeSpan HistoryRetention { get; set; } = TimeSpan.FromHours(2);

        /// <summary>途中経過として「ここまでの返事」を流すか (モデルがストリーミングに対応していれば逐次表示になる)。</summary>
        public bool StreamPartialReplies { get; set; } = true;
    }
}
