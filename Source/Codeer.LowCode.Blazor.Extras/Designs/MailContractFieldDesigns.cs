using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// **差出人 (操作ユーザー) モジュールの契約**。デザインの「現在のユーザーのモジュール」
    /// (AppSettings.CurrentUserModuleDesignName) に1つ置き、「メールアドレス / 表示名」がどの値なのかを宣言する
    /// (UI もデータも持たない)。
    /// </summary>
    /// <remarks>
    /// 使うのは**「自分を差出人にする」(IsFromCurrentUser) の差出人解決**と、
    /// **GmailTokenField のユーザー単位トークン検索** (差出人アドレスで人を引く)。
    /// 差出人の解決はプロバイダ非依存の共通層 (MailDispatcher) が行うので、この契約もプロバイダに依存しない。
    /// 役割の値は自モジュールのフィールドの変数、またはリンクパス ("Email.Value" / "Employee.Email.Value")。
    /// </remarks>
    [Designer(DisplayName = "$MailSenderContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class MailSenderContractFieldDesign : ContractFieldDesignBase
    {
        public MailSenderContractFieldDesign() : base(typeof(MailSenderContractFieldDesign).FullName!) { }

        /// <summary>メールアドレス (必須)。"Email.Value" / リンクパス可。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Variable, DisplayName = "$MailSenderContractEmail")]
        public string Email { get; set; } = "Email.Value";

        /// <summary>差出人の表示名 (任意。空 = 表示名なし)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Variable, DisplayName = "$MailSenderContractDisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        private protected override HashSet<string> VariableRoleNames
            => new() { nameof(Email), nameof(DisplayName) };

        //アドレスだけ必須。表示名は空にすれば使わない
        private protected override HashSet<string> RequiredRoleNames => new() { nameof(Email) };
    }

    /// <summary>
    /// **一斉送信の宛先モジュールの契約**。BulkMailField の宛先リストが指しているモジュールに1つ置き、
    /// 「メールアドレス / 配信停止」がどの値なのかを宣言する (UI もデータも持たない)。
    /// 使うのは一斉送信だけ (単発送信や差出人の解決には関与しない)。
    /// </summary>
    /// <remarks>
    /// 役割の値は**自モジュールのフィールドの変数、またはリンクパス**
    /// ("Email.Value" / 中間テーブル名簿なら "Contact.Email.Value")。リンク先の改名にも追従する。
    /// BulkMailField は宛先リストの先のモジュールにこの契約が無ければデザインチェックでエラーにする
    /// (どのモジュールが宛先かはリストが決めるので、契約はそのモジュールに置く)。
    /// </remarks>
    [Designer(DisplayName = "$BulkMailRecipientContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class BulkMailRecipientContractFieldDesign : ContractFieldDesignBase
    {
        public BulkMailRecipientContractFieldDesign() : base(typeof(BulkMailRecipientContractFieldDesign).FullName!) { }

        /// <summary>メールアドレス (必須)。"Email.Value" / リンクパス "Contact.Email.Value"。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailRecipientContractEmail")]
        public string Email { get; set; } = "Email.Value";

        /// <summary>
        /// 配信停止 (オプトアウト) の Boolean (任意。空 = 判定なし)。true の宛先には送らない (最終安全弁)。
        /// 人の恒久属性を指すのが典型。「今回の対象から外す」は名簿の行を削除するのが正道。
        /// </summary>
        [Designer(Index = 4, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailRecipientContractOptOut")]
        public string OptOut { get; set; } = string.Empty;

        /// <summary>宛先の表示名 (任意。プレビューの一覧で人を見分けるため)。"Name.Value" / リンクパス "Contact.Name.Value"。空 = アドレスだけ。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailRecipientContractDisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        private protected override HashSet<string> VariableRoleNames
            => new() { nameof(Email), nameof(OptOut), nameof(DisplayName) };

        //アドレスだけ必須。配信停止は空にすれば判定しない
        private protected override HashSet<string> RequiredRoleNames => new() { nameof(Email) };
    }

    /// <summary>
    /// メール送信履歴モジュールの契約。履歴モジュールに1つ置き、「役割 → 自モジュールのフィールド名」を
    /// 宣言する (UI もデータも持たない)。役割を空にすると「使わない」宣言 (その項目は記録しない)。
    /// どのモジュールが履歴かの指定はサーバー設定 (appsettings の Mail.HistoryModuleName)。
    /// 契約フィールドが無いモジュールには既定名 (プロパティ初期値) で書く。
    /// </summary>
    [Designer(DisplayName = "$MailHistoryContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class MailHistoryContractFieldDesign : ContractFieldDesignBase
    {
        public MailHistoryContractFieldDesign() : base(typeof(MailHistoryContractFieldDesign).FullName!) { }

        /// <summary>送信日時 (DateTime)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractSentAt")]
        public string SentAt { get; set; } = nameof(SentAt);

        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractMailInfraName")]
        public string MailInfraName { get; set; } = nameof(MailInfraName);

        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractSubject")]
        public string Subject { get; set; } = nameof(Subject);

        /// <summary>送信対象数 (数値)。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractTotalCount")]
        public string TotalCount { get; set; } = nameof(TotalCount);

        /// <summary>成功数 (数値)。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractSuccessCount")]
        public string SuccessCount { get; set; } = nameof(SuccessCount);

        /// <summary>失敗明細 (JSON。Text か Json フィールド)。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractFailureDetails")]
        public string FailureDetails { get; set; } = nameof(FailureDetails);

        /// <summary>送信元レコードのモジュール名。</summary>
        [Designer(Index = 9, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractSourceModule")]
        public string SourceModule { get; set; } = nameof(SourceModule);

        /// <summary>送信元レコードの Id。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractSourceId")]
        public string SourceId { get; set; } = nameof(SourceId);

        /// <summary>
        /// 送信明細の一覧 (任意)。履歴モジュール上の List フィールドで、その参照先が明細モジュール
        /// (MailHistoryDetailContractField を持つ) になる。設定すると 1 宛先 1 行の明細
        /// (宛先・解決後の件名/本文・成否) が書かれる。空 = 明細を残さない。
        /// </summary>
        [Designer(Index = 11, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryContractDetails")]
        public string Details { get; set; } = string.Empty;

        //送信日時だけ必須。他は空にすればその項目は記録しない
        private protected override HashSet<string> RequiredRoleNames => new() { nameof(SentAt) };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            if (!string.IsNullOrEmpty(Details))
                CheckListRole<MailHistoryDetailContractFieldDesign>(context, result, nameof(Details), Details);
            return result;
        }
    }

    /// <summary>
    /// メール送信明細モジュールの契約 (1 宛先 1 行)。履歴契約の Details 一覧の参照先モジュールに1つ置く。
    /// 宛先ごとに**解決後の**件名・本文と成否を記録する (テンプレートの展開結果を確認できる)。
    /// 必須は History (履歴行への Link) と To。他は空にすればその項目は記録しない。
    /// </summary>
    [Designer(DisplayName = "$MailHistoryDetailContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class MailHistoryDetailContractFieldDesign : ContractFieldDesignBase
    {
        public MailHistoryDetailContractFieldDesign() : base(typeof(MailHistoryDetailContractFieldDesign).FullName!) { }

        /// <summary>履歴行への Link (必須)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryDetailContractHistory")]
        public string History { get; set; } = nameof(History);

        /// <summary>宛先アドレス (必須)。リダイレクト送信時も元の宛先。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryDetailContractTo")]
        public string To { get; set; } = nameof(To);

        /// <summary>解決後の件名。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryDetailContractSubject")]
        public string Subject { get; set; } = nameof(Subject);

        /// <summary>解決後の本文 (HTML 本文はそのまま文字列)。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryDetailContractBody")]
        public string Body { get; set; } = nameof(Body);

        /// <summary>送信成否 (Boolean)。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryDetailContractIsSuccess")]
        public string IsSuccess { get; set; } = nameof(IsSuccess);

        /// <summary>失敗理由 (成功時は空)。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$MailHistoryDetailContractError")]
        public string Error { get; set; } = nameof(Error);

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(History), nameof(To) };
    }
}
