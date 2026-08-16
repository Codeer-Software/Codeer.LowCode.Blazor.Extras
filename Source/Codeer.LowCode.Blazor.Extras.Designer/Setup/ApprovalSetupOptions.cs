namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>承認フローセットアップで生成する経路マスタの種類。</summary>
    public enum ApprovalRouteMasterKind
    {
        /// <summary>経路マスタなし (経路はスクリプトで組み立てる)。</summary>
        None,
        /// <summary>シンプル構成 (経路 + ステップの2モジュール。1ステップ1承認者)。</summary>
        Simple,
        /// <summary>標準構成 (経路 + ステップ + 承認者の3モジュール。1ステップ複数承認者)。</summary>
        Standard,
    }

    /// <summary>承認フローセットアップ (モジュール生成 + 申請書結線) のオプション。</summary>
    public class ApprovalSetupOptions
    {
        /// <summary>生成するモジュール名・テーブル名に付けるプレフィックス (空 = なし)。別セットを共存させたい場合に使う。</summary>
        public string Prefix { get; set; } = string.Empty;

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

        /// <summary>順番到達通知メール (メンバーモジュールの MailField + 契約 TurnNotifyMail) を含めるか。</summary>
        public bool UseTurnNotifyMail { get; set; } = true;

        /// <summary>承認待ち一覧などのページリンクを PageFrame に追加するか。</summary>
        public bool AddPageFrameLinks { get; set; } = true;

        /// <summary>結線する申請書モジュール名 (空 = モジュール生成のみ)。</summary>
        public string TargetModuleName { get; set; } = string.Empty;

        /// <summary>申請書モジュールに追加する ApprovalFlowField の名前。</summary>
        public string FieldName { get; set; } = "Approval";

        /// <summary>ApprovalFlowField の FK 列名。</summary>
        public string DbColumn { get; set; } = "approval_id";
    }

    /// <summary>メール履歴モジュール生成のオプション。</summary>
    public class MailHistorySetupOptions
    {
        /// <summary>生成するモジュール名。appsettings の Mail.HistoryModuleName に設定する名前。</summary>
        public string ModuleName { get; set; } = "MailHistory";

        /// <summary>生成するモジュールのデータソース名。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>保護条件 (誰も書けない) の判定に使うユーザーモジュール名。</summary>
        public string UserModuleName { get; set; } = "AppUser";

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

        /// <summary>申請書モジュールへの結線を行ったか。</summary>
        public bool ParentWired { get; set; }

        /// <summary>生成した DDL (テーブル作成 + FK 列追加)。実行はユーザーの確認を挟む。</summary>
        public List<string> Ddl { get; } = new();

        /// <summary>ユーザーへ伝える補足 (スキップ理由・手動作業など)。</summary>
        public List<string> Notes { get; } = new();
    }
}
