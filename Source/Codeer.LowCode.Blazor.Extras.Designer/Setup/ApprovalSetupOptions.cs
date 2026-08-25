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

        /// <summary>
        /// メールを使うか (順番到達通知メール = メンバーモジュールの MailField + 契約 TurnNotifyMail)。
        /// true のときはメールのセットアップ (差出人契約 + 任意で送信履歴モジュール + サーバー設定の案内) も併せて行う。
        /// </summary>
        public bool UseTurnNotifyMail { get; set; } = true;

        /// <summary>メールを使うとき、送信履歴モジュールも生成するか (<see cref="MailSetupOptions.HistoryModuleName"/> の既定名)。</summary>
        public bool UseMailHistory { get; set; } = true;

        /// <summary>承認待ち一覧などのページリンクを PageFrame に追加するか。</summary>
        public bool AddPageFrameLinks { get; set; } = true;

        /// <summary>結線する申請書モジュール名 (空 = モジュール生成のみ)。</summary>
        public string TargetModuleName { get; set; } = string.Empty;

        /// <summary>申請書モジュールに追加する ApprovalFlowField の名前。</summary>
        public string FieldName { get; set; } = "Approval";

        /// <summary>ApprovalFlowField の FK 列名。</summary>
        public string DbColumn { get; set; } = "approval_id";
    }

    /// <summary>メールのセットアップのオプション。</summary>
    public class MailSetupOptions
    {
        /// <summary>テンプレートの対応表 (MailSenderTable) が知っている送信インフラの呼び名。</summary>
        public static readonly string[] InfraNames = ["Smtp", "GraphApi", "SendGrid", "Gmail"];

        /// <summary>差出人 (操作ユーザー) のモジュール名。デザインの「現在のユーザーのモジュール」。</summary>
        public string UserModuleName { get; set; } = "AppUser";

        /// <summary>ユーザーモジュールのメールアドレスフィールド名 (差出人契約の Email 役割)。</summary>
        public string UserEmailField { get; set; } = "Email";

        /// <summary>ユーザーモジュールの表示名フィールド名 (差出人契約の DisplayName 役割)。</summary>
        public string UserDisplayNameField { get; set; } = "Name";

        /// <summary>差出人契約 (MailSenderContractField) をユーザーモジュールに追加するか。</summary>
        public bool AddSenderContract { get; set; } = true;

        /// <summary>Gmail ユーザートークン欄 (GmailTokenField) をユーザーモジュールに追加するか (Gmail で本人名義に送る場合のみ)。</summary>
        public bool AddGmailTokenField { get; set; }

        /// <summary>送信履歴モジュールを生成するか。</summary>
        public bool CreateHistoryModule { get; set; } = true;

        /// <summary>送信履歴モジュール名。appsettings の Mail.HistoryModuleName に設定する名前。</summary>
        public string HistoryModuleName { get; set; } = "MailHistory";

        /// <summary>生成するモジュールのデータソース名。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>送信履歴ページのリンクを PageFrame に追加するか。</summary>
        public bool AddPageFrameLink { get; set; } = true;

        /// <summary>既定の送信インフラの呼び名 (appsettings 案内の Mail.DefaultInfraName とプロバイダセクション雛形に使う)。</summary>
        public string DefaultInfraName { get; set; } = "Smtp";
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

        /// <summary>別のセットアップの結果を取り込む (承認フローのセットアップがメールのセットアップを内包するとき)。</summary>
        public void Merge(SetupResult other)
        {
            CreatedModules.AddRange(other.CreatedModules);
            SkippedModules.AddRange(other.SkippedModules);
            Ddl.AddRange(other.Ddl);
            Notes.AddRange(other.Notes);
        }

        /// <summary>生成した DDL (テーブル作成 + FK 列追加)。実行はユーザーの確認を挟む。</summary>
        public List<string> Ddl { get; } = new();

        /// <summary>ユーザーへ伝える補足 (スキップ理由・手動作業など)。</summary>
        public List<string> Notes { get; } = new();
    }
}
