using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
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
        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string SentAt { get; set; } = nameof(SentAt);

        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string SenderName { get; set; } = nameof(SenderName);

        [Designer(Index = 5, CandidateType = CandidateType.Field)]
        public string Subject { get; set; } = nameof(Subject);

        /// <summary>送信対象数 (数値)。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field)]
        public string TotalCount { get; set; } = nameof(TotalCount);

        /// <summary>成功数 (数値)。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field)]
        public string SuccessCount { get; set; } = nameof(SuccessCount);

        /// <summary>失敗明細 (JSON。Text か Json フィールド)。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field)]
        public string FailureDetails { get; set; } = nameof(FailureDetails);

        /// <summary>送信元レコードのモジュール名。</summary>
        [Designer(Index = 9, CandidateType = CandidateType.Field)]
        public string SourceModule { get; set; } = nameof(SourceModule);

        /// <summary>送信元レコードの Id。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.Field)]
        public string SourceId { get; set; } = nameof(SourceId);
    }
}
