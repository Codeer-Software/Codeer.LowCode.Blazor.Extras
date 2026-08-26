using Codeer.LowCode.Blazor.DesignLogic;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// 承認メンバー契約の任意役割 (ポリシー系) が空のときにエンジン・UI が使う既定値。
    /// 役割を空にする = 「そのアプリはその概念を使わない」宣言 (経路マスタ側に同名の設定があってもメンバー行には残らず、ここの既定で動く)。
    /// </summary>
    public static class ApprovalMemberDefaults
    {
        /// <summary>ステップ種別: 承認 (回覧の概念を持たないアプリ)。</summary>
        public static readonly string StepType = ApprovalStepType.Approval.ToDesignValue();

        /// <summary>ステップ完了条件: 必須メンバー全員承認。</summary>
        public static readonly string CompletionPolicy = ApprovalCompletionPolicy.RequiredMembers.ToDesignValue();

        /// <summary>差し戻し先: 申請者のみ。</summary>
        public static readonly string ReturnScope = ApprovalReturnScope.ApplicantOnly.ToDesignValue();

        /// <summary>却下時のコメント: 任意。</summary>
        public const bool IsCommentRequiredOnReject = false;

        /// <summary>必須承認者か: 全員必須 (列を持たないアプリは「任意承認者」の概念が無い)。</summary>
        public const bool IsRequired = true;
    }
}
