using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// フロー全体の状態 (承認フローモジュールの Status 値)。
    /// [DesignEnum] によりデザイン enum として公開される (enum 定義ファイル不要。
    /// SelectField の EnumName・条件エディタの値候補・スクリプトから使える)。
    /// メンバー名 = DB 保存値のプロトコル値のため変更不可。
    /// 「取消」状態は持たない (完全にやめたい場合は取り下げてレコードを削除する)。
    /// </summary>
    [DesignEnum]
    public enum ApprovalFlowStatus
    {
        [DesignEnumMember(DisplayText = "$ApprovalFlowStatus_InProgress")]
        InProgress,
        [DesignEnumMember(DisplayText = "$ApprovalFlowStatus_Completed")]
        Completed,
        [DesignEnumMember(DisplayText = "$ApprovalFlowStatus_Rejected")]
        Rejected,
        [DesignEnumMember(DisplayText = "$ApprovalFlowStatus_Returned")]
        Returned,
        [DesignEnumMember(DisplayText = "$ApprovalFlowStatus_Withdrawn")]
        Withdrawn,
    }

    /// <summary>フロー状態 (保存値 = 文字列) に対する判定。</summary>
    public static class ApprovalFlowStatusLogic
    {
        /// <summary>申請内容を編集して再申請できる状態か。</summary>
        public static bool CanResubmit(string? status)
            => Enum.TryParse<ApprovalFlowStatus>(status, out var s) &&
               s is ApprovalFlowStatus.Rejected or ApprovalFlowStatus.Returned or ApprovalFlowStatus.Withdrawn;
    }

    /// <summary>
    /// 承認メンバーの状態 (承認メンバーモジュールの Status 値)。デザイン enum として公開。
    /// Waiting は「本当に今待っている人」だけ (未到達ステップは Pending)。
    /// 承認待ち一覧は Status = Waiting を検索するだけで正確になる。
    /// </summary>
    [DesignEnum]
    public enum ApprovalMemberStatus
    {
        /// <summary>未到達 (前の承認ステップが完了したら Waiting に昇格する)。</summary>
        [DesignEnumMember(DisplayText = "$ApprovalMemberStatus_Pending")]
        Pending,
        [DesignEnumMember(DisplayText = "$ApprovalMemberStatus_Waiting")]
        Waiting,
        [DesignEnumMember(DisplayText = "$ApprovalMemberStatus_Approved")]
        Approved,
        [DesignEnumMember(DisplayText = "$ApprovalMemberStatus_Rejected")]
        Rejected,
        [DesignEnumMember(DisplayText = "$ApprovalMemberStatus_Confirmed")]
        Confirmed,
        [DesignEnumMember(DisplayText = "$ApprovalMemberStatus_Skipped")]
        Skipped,
    }

    /// <summary>ステップ種別。デザイン enum として公開。Confirmation (回覧) はフローの進行をブロックしない。</summary>
    [DesignEnum]
    public enum ApprovalStepType
    {
        [DesignEnumMember(DisplayText = "$ApprovalStepType_Approval")]
        Approval,
        [DesignEnumMember(DisplayText = "$ApprovalStepType_Confirmation")]
        Confirmation,
    }


    /// <summary>ステップの完了条件 (メンバー行に保存)。デザイン enum として公開。</summary>
    [DesignEnum]
    public enum ApprovalCompletionPolicy
    {
        /// <summary>必須メンバー全員承認。必須ゼロなら任意1人 (現行テンプレート互換)。</summary>
        [DesignEnumMember(DisplayText = "$ApprovalCompletionPolicy_RequiredMembers")]
        RequiredMembers,
        [DesignEnumMember(DisplayText = "$ApprovalCompletionPolicy_All")]
        All,
        [DesignEnumMember(DisplayText = "$ApprovalCompletionPolicy_Any")]
        Any,
    }

    /// <summary>差し戻し先の許可範囲 (ステップ設定。メンバー行に保存)。デザイン enum として公開。</summary>
    [DesignEnum]
    public enum ApprovalReturnScope
    {
        [DesignEnumMember(DisplayText = "$ApprovalReturnScope_ApplicantOnly")]
        ApplicantOnly,
        [DesignEnumMember(DisplayText = "$ApprovalReturnScope_AnyPreviousStep")]
        AnyPreviousStep,
    }

    /// <summary>
    /// 取り下げを許可する範囲 (業務ポリシー = デザインで可変)。
    /// </summary>
    public enum ApprovalWithdrawPolicy
    {
        /// <summary>承認が始まる前のみ (Garoon 等の「取り戻し」と同じ。既定)。</summary>
        BeforeFirstApproval,

        /// <summary>進行中ならいつでも。</summary>
        Anytime,
    }

    /// <summary>
    /// アクション名 (command API のパスセグメント / 履歴の Action 値)。デザイン enum として公開。
    /// 表示名はアクションボタンのリソースを共用する。
    /// </summary>
    [DesignEnum]
    public enum ApprovalAction
    {
        [DesignEnumMember(DisplayText = "$ApprovalAction_Submit")]
        Submit,
        [DesignEnumMember(DisplayText = "$ApprovalAction_Approve")]
        Approve,
        [DesignEnumMember(DisplayText = "$ApprovalAction_Reject")]
        Reject,
        [DesignEnumMember(DisplayText = "$ApprovalAction_Return")]
        Return,

        /// <summary>取り下げ (承認が始まる前のみ。編集して再申請できる)。</summary>
        [DesignEnumMember(DisplayText = "$ApprovalAction_Withdraw")]
        Withdraw,

        [DesignEnumMember(DisplayText = "$ApprovalAction_Resubmit")]
        Resubmit,
        [DesignEnumMember(DisplayText = "$ApprovalAction_Confirm")]
        Confirm,
    }
}
