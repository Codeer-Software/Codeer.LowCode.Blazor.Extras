namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// フロー全体の状態 (ApprovalFlow モジュールの Status 値)。
    /// 「取消」状態は持たない (完全にやめたい場合は取り下げてレコードを削除する)。
    /// </summary>
    public static class ApprovalFlowStatuses
    {
        public const string InProgress = nameof(InProgress);
        public const string Completed = nameof(Completed);
        public const string Rejected = nameof(Rejected);
        public const string Returned = nameof(Returned);
        public const string Withdrawn = nameof(Withdrawn);

        /// <summary>申請内容を編集して再申請できる状態か。</summary>
        public static bool CanResubmit(string? status)
            => status is Rejected or Returned or Withdrawn;
    }

    /// <summary>
    /// 承認メンバーの状態 (ApprovalFlowMember モジュールの Status 値)。
    /// Waiting は「本当に今待っている人」だけ (未到達ステップは Pending)。
    /// 承認待ち一覧は Status = Waiting を検索するだけで正確になる。
    /// </summary>
    public static class ApprovalMemberStatuses
    {
        /// <summary>未到達 (前の承認ステップが完了したら Waiting に昇格する)。</summary>
        public const string Pending = nameof(Pending);

        public const string Waiting = nameof(Waiting);
        public const string Approved = nameof(Approved);
        public const string Rejected = nameof(Rejected);
        public const string Confirmed = nameof(Confirmed);
        public const string Skipped = nameof(Skipped);
    }

    /// <summary>ステップ種別。Confirmation (回覧) はフローの進行をブロックしない。</summary>
    public static class ApprovalStepTypes
    {
        public const string Approval = nameof(Approval);
        public const string Confirmation = nameof(Confirmation);
    }

    /// <summary>ステップの完了条件。</summary>
    public static class ApprovalCompletionPolicies
    {
        /// <summary>必須メンバー全員承認。必須ゼロなら任意1人 (現行テンプレート互換)。</summary>
        public const string RequiredMembers = nameof(RequiredMembers);
        public const string All = nameof(All);
        public const string Any = nameof(Any);
    }

    /// <summary>差し戻し先の許可範囲 (ステップ設定)。</summary>
    public static class ApprovalReturnScopes
    {
        public const string ApplicantOnly = nameof(ApplicantOnly);
        public const string AnyPreviousStep = nameof(AnyPreviousStep);
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

    /// <summary>アクション名 (command API のパスセグメント / 履歴の Action 値)。</summary>
    public static class ApprovalActions
    {
        public const string Submit = nameof(Submit);
        public const string Approve = nameof(Approve);
        public const string Reject = nameof(Reject);
        public const string Return = nameof(Return);

        /// <summary>取り下げ (承認が始まる前のみ。編集して再申請できる)。</summary>
        public const string Withdraw = nameof(Withdraw);

        public const string Resubmit = nameof(Resubmit);
        public const string Confirm = nameof(Confirm);
    }

    /// <summary>
    /// 承認データモジュールの既定フィールド名。
    /// v1 はこの名前で固定 (モジュール名のみフィールドのプロパティで指定可能)。
    /// 役割→フィールド名のマッピング指定は自動生成機能と合わせて拡張予定。
    /// </summary>
    public static class ApprovalFieldNames
    {
        /// <summary>ApprovalFlow モジュール。</summary>
        public static class Flow
        {
            public const string Status = nameof(Status);
            public const string TargetModuleName = nameof(TargetModuleName);
            public const string TargetId = nameof(TargetId);
            public const string RouteName = nameof(RouteName);
            public const string AttemptNo = nameof(AttemptNo);
            public const string CurrentStepNo = nameof(CurrentStepNo);
        }

        /// <summary>ApprovalFlowMember モジュール (ステップ情報は行に非正規化スナップショット)。</summary>
        public static class Member
        {
            public const string Flow = nameof(Flow);
            public const string AttemptNo = nameof(AttemptNo);
            public const string StepNo = nameof(StepNo);
            public const string StepName = nameof(StepName);
            public const string StepType = nameof(StepType);
            public const string CompletionPolicy = nameof(CompletionPolicy);
            public const string IsCommentRequiredOnReject = nameof(IsCommentRequiredOnReject);
            public const string ReturnScope = nameof(ReturnScope);
            public const string ApproverUser = nameof(ApproverUser);
            public const string IsRequired = nameof(IsRequired);

            /// <summary>最終承認ステップのメンバーか (条件式で「最終承認者」を表すためのスナップショット)。</summary>
            public const string IsFinalStep = nameof(IsFinalStep);
            public const string Status = nameof(Status);
            public const string ActedAt = nameof(ActedAt);
        }

        /// <summary>ApprovalHistory モジュール (不変。エンジンは INSERT のみ)。</summary>
        public static class History
        {
            public const string Flow = nameof(Flow);
            public const string AttemptNo = nameof(AttemptNo);
            public const string StepNo = nameof(StepNo);
            public const string Action = nameof(Action);
            public const string ActorUser = nameof(ActorUser);
            public const string FromStatus = nameof(FromStatus);
            public const string ToStatus = nameof(ToStatus);
            public const string Comment = nameof(Comment);
            public const string ActedAt = nameof(ActedAt);
        }
    }
}
