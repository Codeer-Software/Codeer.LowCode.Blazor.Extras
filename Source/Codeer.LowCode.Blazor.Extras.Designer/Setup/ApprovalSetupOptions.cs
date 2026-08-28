namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>承認フローセットアップで生成する経路マスタの種類。</summary>
    public enum ApprovalRouteMasterKind
    {
        /// <summary>経路マスタなし (経路はスクリプトで組み立てる)。</summary>
        None,
        /// <summary>経路マスタあり (経路 + ステップ + 承認者の3モジュール。契約なしのただのモジュール + それを読む OnBuildRoute 雛形)。</summary>
        Standard,
    }

    /// <summary>承認フローセットアップ (承認モジュール群の生成) のオプション。</summary>
    public class ApprovalSetupOptions
    {
        /// <summary>生成するモジュールのデータソース名。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>承認者・申請者のリンク先になるユーザーモジュール名。</summary>
        public string UserModuleName { get; set; } = "AppUser";

        /// <summary>ユーザーモジュールの表示名フィールド名 (リンクの表示・通知メールの宛先表示に使う)。</summary>
        public string UserDisplayNameField { get; set; } = "Name";

        /// <summary>ユーザーモジュールのメールアドレスフィールド名 (通知メールの宛先に使う)。</summary>
        public string UserEmailField { get; set; } = "Email";

        /// <summary>生成する経路マスタの種類。</summary>
        public ApprovalRouteMasterKind RouteMaster { get; set; } = ApprovalRouteMasterKind.Standard;

        /// <summary>
        /// 順番到達通知メール (メンバーモジュールの MailField + 契約 TurnNotifyMail) を含めるか。
        /// メール側の準備 (送信履歴・サーバー設定) はメールのセットアップで先に済ませておく。
        /// </summary>
        public bool UseTurnNotifyMail { get; set; } = true;

        /// <summary>承認待ち一覧などのページリンクを PageFrame に追加するか。</summary>
        public bool AddPageFrameLinks { get; set; } = true;
    }

    /// <summary>メールのセットアップのオプション。</summary>
    public class MailSetupOptions
    {
        /// <summary>送信履歴モジュールを生成するか。</summary>
        public bool CreateHistoryModule { get; set; } = true;

        /// <summary>送信履歴モジュール名。appsettings の Mail.HistoryModuleName に設定する名前。</summary>
        public string HistoryModuleName { get; set; } = "MailHistory";

        /// <summary>送信明細モジュール (1 宛先 1 行。解決後の件名・本文と成否) も生成するか (履歴モジュールを生成するときだけ)。</summary>
        public bool CreateHistoryDetailModule { get; set; } = true;

        /// <summary>送信明細モジュール名。</summary>
        public string HistoryDetailModuleName { get; set; } = "MailHistoryDetail";

        /// <summary>生成するモジュールのデータソース名。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>送信履歴ページのリンクを PageFrame に追加するか。</summary>
        public bool AddPageFrameLink { get; set; } = true;
    }

    /// <summary>セットアップの実行結果。</summary>
    public class SetupResult
    {
        /// <summary>生成したモジュール名。</summary>
        public List<string> CreatedModules { get; } = new();

        /// <summary>既存のため生成をスキップしたモジュール名 (使いまわし)。</summary>
        public List<string> SkippedModules { get; } = new();

        /// <summary>生成した DDL (テーブル作成・列追加)。実行はユーザーの確認を挟む。</summary>
        public List<string> Ddl { get; } = new();

        /// <summary>ユーザーへ伝える補足 (スキップ理由・手動作業など)。</summary>
        public List<string> Notes { get; } = new();
    }
}
