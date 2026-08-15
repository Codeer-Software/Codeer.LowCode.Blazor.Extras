using Codeer.LowCode.Blazor.DataIO;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// 申請 / 再申請の要求。申請書の保存データを同梱し、サーバーが
    /// 「親保存 → 経路検証 → フロー生成 → FK 設定 → 履歴」を同一トランザクションで行う。
    /// </summary>
    public class ApprovalSubmitRequest
    {
        /// <summary>申請書モジュール名。</summary>
        public string TargetModuleName { get; set; } = string.Empty;

        /// <summary>申請書モジュール上の ApprovalFlowField 名 (サーバーはこのデザインからモジュール名等を解決する)。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>申請書の保存データ (Module.GetSubmitData)。</summary>
        public ModuleSubmitData? TargetSubmitData { get; set; }

        /// <summary>スクリプトで組み立てた経路。誰が組んだかは履歴に不変記録される。</summary>
        public ApprovalRouteData? Route { get; set; }

        public string Comment { get; set; } = string.Empty;

        /// <summary>再申請時のみ: 対象フローの Id。</summary>
        public string FlowId { get; set; } = string.Empty;

        /// <summary>再申請時のみ: 楽観ロック検証値 (フロー行の OptimisticLocking 値)。</summary>
        public string ExpectedVersion { get; set; } = string.Empty;
    }

    /// <summary>承認・却下・差し戻し・取り戻し・取消・確認の要求。</summary>
    public class ApprovalActionRequest
    {
        public string TargetModuleName { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string FlowId { get; set; } = string.Empty;

        /// <summary>楽観ロック検証値。二重承認・同時操作の防止に必須。</summary>
        public string ExpectedVersion { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;

        /// <summary>差し戻し先ステップ番号 (Return のみ。null = 申請者へ)。</summary>
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

        public static ApprovalActionResult Success(string flowId, string targetId)
            => new() { IsSuccess = true, FlowId = flowId, TargetId = targetId };

        public static ApprovalActionResult Failure(string message)
            => new() { ErrorMessage = message };
    }
}
