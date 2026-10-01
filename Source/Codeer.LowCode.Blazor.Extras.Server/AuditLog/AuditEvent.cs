namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>監査ログの分類。何を記録するかは呼び出し側 (WebAPI の入口) が <see cref="AuditAttribute"/> で宣言する。</summary>
    public enum AuditCategory
    {
        /// <summary>ログイン・ログアウト・二要素認証。</summary>
        Authentication,
        /// <summary>レコードの参照 (一覧・詳細)。</summary>
        DataRead,
        /// <summary>レコードの変更 (追加・更新・削除・一括取込・アップロード)。</summary>
        DataWrite,
        /// <summary>外へ出す操作 (ファイル出力・PDF・添付ファイルのダウンロード・メール送信)。</summary>
        Export,
        /// <summary>管理操作 (再索引・設定の変化)。</summary>
        Admin,
        /// <summary>システムのイベント (起動・停止・監査ログの掃除)。</summary>
        System,
        /// <summary>宣言のない API。</summary>
        Other,
    }

    /// <summary>
    /// 操作の結果。Attempt と Continued は「結果」ではない (Attempt = 操作の前に書く試行の記録、Continued = 対象の続きの行)。
    /// 操作の件数は Success / Failure / Denied の行で数える。
    /// </summary>
    public enum AuditResult
    {
        /// <summary>操作の前に書く試行の記録。誰が・どこから・どの API を呼んだか。結果は同じ RequestId の後段の行にある。</summary>
        Attempt,
        Success,
        /// <summary>例外・エラー応答 (HTTP 4xx/5xx。認可拒否を除く)・保存結果のエラー。</summary>
        Failure,
        /// <summary>認証・認可で拒否された (HTTP 401/403、ログイン失敗、二要素認証のコード不一致)。</summary>
        Denied,
        /// <summary>
        /// 対象の続きの行。1 レコードに入れる対象は <see cref="AuditLogger.MaxTargetsPerRecord"/> 件までで、
        /// 超えた分は同じ RequestId のこの行に分けて書く (切り捨てない)。結果は同じ RequestId の結果の行にある。
        /// </summary>
        Continued,
    }

    /// <summary>監査ログの 1 レコード。いつ・誰が・どこから・何に・何を・結果。</summary>
    public class AuditEvent
    {
        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
        public AuditCategory Category { get; set; } = AuditCategory.Other;
        /// <summary>操作の名前。WebAPI なら "Controller.Action" (例 "ModuleData.Submit")。</summary>
        public string Action { get; set; } = string.Empty;
        public AuditResult Result { get; set; } = AuditResult.Success;
        /// <summary>操作したユーザーの Id (ユーザーモジュールの行の Id)。未認証なら空。</summary>
        public string UserId { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        /// <summary>リクエストの識別子 (ASP.NET の TraceIdentifier)。アプリのログと突き合わせる鍵。</summary>
        public string RequestId { get; set; } = string.Empty;
        /// <summary>発生したサーバー (複数インスタンス運用での発生元)。</summary>
        public string Host { get; set; } = Environment.MachineName;
        /// <summary>
        /// この操作が使ったデザインの版 (App.zip の SHA-256)。ホストが版を渡していなければ空。
        /// リクエストの間は変わらないので、試行の行と結果の行は同じ値になる。
        /// </summary>
        public string DesignVersion { get; set; } = string.Empty;
        /// <summary>対象のレコード (モジュール名・Id・操作)。多いときは続きの行 (Result = Continued) に分かれる。</summary>
        public List<AuditTarget> Targets { get; set; } = new();
        /// <summary>補足 (失敗の理由・試行したログイン名・件数など)。値そのものは入れない。</summary>
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>操作の対象。Id が無い操作 (一覧・一括) はモジュール名だけ。</summary>
    public class AuditTarget
    {
        public string Module { get; set; } = string.Empty;
        public string? Id { get; set; }
        /// <summary>対象への操作 (Read / Add / Update / Delete / Download / Upload / Import / Export など)。</summary>
        public string Operation { get; set; } = string.Empty;
    }
}
