namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// appsettings の AuditLog セクション (1 つ)。出力先は <see cref="Database"/> / <see cref="File"/> の入れ子で、使うものだけ書く。
    /// 何を記録するかは設定ではなくコード (WebAPI の <see cref="AuditAttribute"/>) が決める: 分類を宣言したアクションは全部記録し、
    /// 宣言の無いアクションは失敗と拒否だけ。試行の行 (Attempt) を書くかは分類で決まる (<see cref="AuditLogger.HasAttempt"/>)。
    /// 保持期限の削除は持たない (監査ログは追記専用。古い行の扱いは DB 管理者の運用)。
    /// </summary>
    public class AuditLogSettings
    {
        public bool Enabled { get; set; }

        /// <summary>書き込みに失敗したときの扱い。既定は Strict (その操作を失敗にする)。</summary>
        public AuditFailureMode FailureMode { get; set; } = AuditFailureMode.Strict;

        /// <summary>DB への出力 (DataSourceName が空なら書かない)。</summary>
        public AuditLogDatabaseSettings Database { get; set; } = new();

        /// <summary>ファイルへの出力 (Directory が空なら書かない)。</summary>
        public AuditLogFileSettings File { get; set; } = new();
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

    /// <summary>AuditLog.Database。DataSourceName が空なら DB には書かない。</summary>
    public class AuditLogDatabaseSettings
    {
        /// <summary>書き込み先のデータソース名 (DataSources の Name)。業務データと分け、INSERT だけできる DB ユーザーで繋ぐことを推奨。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>テーブル名。列は固定 (<see cref="DatabaseAuditSink.CreateTableSql"/>)。</summary>
        public string Table { get; set; } = "audit_log";
    }

    /// <summary>AuditLog.File。Directory が空ならファイルには書かない。</summary>
    public class AuditLogFileSettings
    {
        /// <summary>JSON Lines を置くフォルダ。ホスト名と日付ごとに 1 ファイル。SIEM 等への転送元にする。</summary>
        public string Directory { get; set; } = string.Empty;
    }
}
