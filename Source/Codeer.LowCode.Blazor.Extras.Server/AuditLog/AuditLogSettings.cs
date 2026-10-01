namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>appsettings の AuditLog セクション。出力先は種類ごとの別セクション (<see cref="AuditLogDatabaseSettings"/> / <see cref="AuditLogFileSettings"/>)。</summary>
    public class AuditLogSettings
    {
        public bool Enabled { get; set; }

        /// <summary>書き込みに失敗したときの扱い。既定は Strict (その操作を失敗にする)。</summary>
        public AuditFailureMode FailureMode { get; set; } = AuditFailureMode.Strict;

        /// <summary>保持日数。これより古いレコードを 1 日 1 回消す。0 なら消さない。</summary>
        public int RetentionDays { get; set; }

        /// <summary>
        /// 記録する分類。空なら全部。ここに無い分類は成功した操作だけ記録しない
        /// (失敗・拒否は分類に関係なく常に記録する)。
        /// </summary>
        public AuditCategory[] Categories { get; set; } = [];

        /// <summary>
        /// 操作の前に試行の行 (Result = Attempt) を書く分類。前段が書けなければ操作を実行しない (Strict) ので、
        /// この分類の操作は「記録の無い操作」が起きない。省略 (null) なら既定 = 状態を変える・外へ出す操作と認証
        /// (<see cref="DefaultAttemptCategories"/>)。Categories で記録しない分類には前段も書かない。
        /// 設定の配列は既定の配列に継ぎ足されるので、既定値はプロパティに持たず null で表す (appsettings の空配列は省略と同じ)。
        /// </summary>
        public AuditCategory[]? AttemptCategories { get; set; }

        public static readonly AuditCategory[] DefaultAttemptCategories = [AuditCategory.Authentication, AuditCategory.DataWrite, AuditCategory.Export, AuditCategory.Admin];

        /// <summary>有効な試行の分類 (省略なら既定)。</summary>
        public AuditCategory[] EffectiveAttemptCategories => AttemptCategories ?? DefaultAttemptCategories;
    }

    public enum AuditFailureMode
    {
        /// <summary>
        /// 1 つでも出力先に書けなければ操作を失敗にする (レスポンスは 500)。監査ログの障害でアプリを止める運用。
        /// 前段 (Attempt) が書けなければ操作を実行しない。後段が書けなかった操作は前段だけが残る (結果は ILogger の Critical にある)。
        /// </summary>
        Strict,
        /// <summary>書けなくてもアプリのログ (ILogger の Critical) に残して操作は続ける。</summary>
        BestEffort,
    }

    /// <summary>appsettings の AuditLogDatabase セクション。DataSourceName が空なら DB には書かない。</summary>
    public class AuditLogDatabaseSettings
    {
        /// <summary>書き込み先のデータソース名 (DataSources の Name)。業務データと分け、INSERT だけできる DB ユーザーで繋ぐことを推奨。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>テーブル名。列は固定 (<see cref="DatabaseAuditSink.CreateTableSql"/>)。</summary>
        public string Table { get; set; } = "audit_log";
    }

    /// <summary>appsettings の AuditLogFile セクション。Directory が空ならファイルには書かない。</summary>
    public class AuditLogFileSettings
    {
        /// <summary>JSON Lines を置くフォルダ。ホスト名と日付ごとに 1 ファイル。SIEM 等への転送元にする。</summary>
        public string Directory { get; set; } = string.Empty;
    }
}
