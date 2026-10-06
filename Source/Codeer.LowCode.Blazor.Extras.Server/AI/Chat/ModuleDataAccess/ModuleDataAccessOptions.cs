namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// <see cref="ModuleDataAccessAgent"/> の設定 (文字列と数値だけ。appsettings のセクションからそのまま束縛できる)。
    /// 行の読み方 (実行ユーザーの ModuleDataIO の開き方)・デザイン定義・モデル・ログはこの設定ではなく依存なので、Agent のコンストラクタで渡す。
    /// </summary>
    public class ModuleDataAccessOptions
    {
        /// <summary>find_records が 1 回で AI に返す行数の上限 (AI が指定する limit もこれで頭打ち)。</summary>
        public int MaxRows { get; set; } = 200;

        /// <summary>1 回のツール結果として AI に返す文字数の上限 (トークンの歯止め。超えたら行を半分に減らして truncated=true)。</summary>
        public int MaxResultChars { get; set; } = 20000;

        /// <summary>aggregate_records が返すグループ数の上限 (AI が指定する limit もこれで頭打ち。超えた分は並び順の先頭から切って limited=true で知らせる)。集計は DB 側で行う。</summary>
        public int MaxGroups { get; set; } = 100;

        /// <summary>cross_tab の表のセル数 (行の種類 × 列の種類 × 値の数) の上限。超えたら表を作らずにエラーで知らせる。</summary>
        public int MaxCrossTabCells { get; set; } = 2000;

        /// <summary>年度の開始月 (1〜12)。日付を年・四半期でまとめるときの既定 (AI がグループごとに指定すればそちら)。1 なら暦年。</summary>
        public int FiscalYearStartMonth { get; set; } = 1;

        // ---- DB の負荷の上限 (0 で無効。待たせる・断る種類のものは既定で無効) ----

        /// <summary>
        /// レコードを読む SQL 1 文のタイムアウト (秒)。<see cref="ModuleDataAccessScope"/> に渡された IDbAccessor の CommandTimeoutSeconds に入れる
        /// (渡されていなければ効かない)。0 でドライバの既定。
        /// </summary>
        public int CommandTimeoutSeconds { get; set; }

        /// <summary>1 回の返事でレコードの読み取り (find_records / aggregate_records / cross_tab / get_record) に使える合計時間 (秒)。使い切ったら以後は DB へ行かずに断る。0 で無制限 (既定)。</summary>
        public int MaxQuerySecondsPerReply { get; set; }

        /// <summary>同じデータソースへ同時に実行する読み取りの本数 (プロセス全体。RawDataAccessAgent と共有)。超えた分は空くまで待つ。0 で無制限 (既定)。</summary>
        public int MaxConcurrentQueries { get; set; }

        /// <summary>システムプロンプトに追記する業務固有の説明 (用語、よく聞かれる集計の定義など)。文書として渡すなら <see cref="AIChatDocument"/> のほうが管理しやすい。</summary>
        public string AdditionalInstructions { get; set; } = string.Empty;

        // ---- 会話 (モデルとのやりとり) の設定 ----

        /// <summary>
        /// システムプロンプト。この後にツールの説明 (設計参照・レコード参照・グラフ) と依頼したユーザー名が続く。
        /// 既定は「設計と補足文書で業務の意味を確かめ、ユーザーが見られる範囲のレコードを読んで答えるアシスタント」(日本語)。
        /// </summary>
        public string SystemPrompt { get; set; } =
            "あなたは業務 Web アプリケーションに組み込まれたアシスタントです。アプリの設計 (モジュール定義) と補足文書で業務の意味を確かめ、アプリのレコードを読んで、データに関する質問に答えます。" +
            "読めるのは、今のユーザーがアプリの画面で見られるレコードと項目だけです。権限が無くて読めなかったときは、その旨を伝えてください (回避しようとしないこと)。" +
            "質問に出てくる用語 (売上、在庫など) は、補足文書に定義があればその定義に従い、設計の候補値 (状態コード等) で条件に落としてください。定義を確かめずに項目名だけで解釈しないこと。" +
            "ユーザーの言語で、簡潔に、Markdown で答えてください。使った数字は表で示し、短い解釈を添えてください。" +
            "数字を作らないこと。すべての数値はツールの結果に基づくものだけにしてください。データで答えられない質問には、その旨と代わりに確認できることを伝えてください。";

        /// <summary>1 回の返事で許すツール呼び出し (レコード参照・設計参照・グラフ) の往復回数の上限 (暴走とコストの歯止め)。</summary>
        public int MaxToolCallRoundsPerReply { get; set; } = 10;

        /// <summary>会話履歴として保持するユーザー発言の数 (古いターンから捨てる。ツール呼び出しと結果は同じターンとして一緒に扱う)。</summary>
        public int MaxHistoryTurns { get; set; } = 20;

        /// <summary>
        /// ツール呼び出しと結果 (レコードの JSON 等、履歴の中で最も大きい) を残す直近ターン数。それより古いターンはモデルの文章だけ残す。
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
