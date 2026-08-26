using Codeer.LowCode.Blazor.DataIO;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// command API の要求 (1 エンドポイント)。Action で操作を指定し、サーバー (ApprovalEngine) が振り分ける。
    /// Submit / Resubmit は申請書の保存データを同梱し、サーバーが
    /// 「親保存 → 経路検証 → フロー生成 → FK 設定 → 履歴」を同一トランザクションで行う。
    /// </summary>
    public class ApprovalCommand
    {
        /// <summary>操作。</summary>
        public ApprovalAction Action { get; set; }

        /// <summary>申請書モジュール名。</summary>
        public string TargetModuleName { get; set; } = string.Empty;

        /// <summary>申請書モジュール上の ApprovalFlowField 名 (サーバーはこのデザインからモジュール名等を解決する)。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>対象フローの Id (Submit 以外で必須)。</summary>
        public string FlowId { get; set; } = string.Empty;

        /// <summary>楽観ロック検証値 (フロー行の OptimisticLocking 値。Submit 以外で必須。二重承認・同時操作の防止)。</summary>
        public string ExpectedVersion { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;

        /// <summary>Submit / Resubmit のみ: 申請書の保存データ (Module.GetSubmitData)。</summary>
        public ModuleSubmitData? TargetSubmitData { get; set; }

        /// <summary>Submit / Resubmit のみ: スクリプトで組み立てた経路。誰が組んだかは履歴に不変記録される。</summary>
        public ApprovalRouteData? Route { get; set; }

        /// <summary>Return のみ: 差し戻し先ステップ番号 (null = 申請者へ)。</summary>
        public int? TargetStepNo { get; set; }
    }

    /// <summary>command API の応答。</summary>
    public class ApprovalActionResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>対象フローの Id (申請時は新規作成されたフロー)。</summary>
        public string FlowId { get; set; } = string.Empty;

        /// <summary>申請書レコードの Id (申請時は保存で確定した実 Id)。</summary>
        public string TargetId { get; set; } = string.Empty;

        internal static ApprovalActionResult Success(string flowId, string targetId)
            => new() { IsSuccess = true, FlowId = flowId, TargetId = targetId };

        internal static ApprovalActionResult Failure(string message)
            => new() { ErrorMessage = message };
    }
}
