using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 承認フローモジュールの契約。Members / Histories の一覧の先がメンバー・履歴モジュールになる。
    /// 役割は「エンジン・UI が読むもの」だけ (書くだけの項目は契約に持たない) なので全て必須。
    /// </summary>
    [Designer(DisplayName = "$ApprovalFlowContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalFlowContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalFlowContractFieldDesign() : base(typeof(ApprovalFlowContractFieldDesign).FullName!) { }

        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractStatus")]
        public string Status { get; set; } = nameof(Status);

        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractTargetModuleName")]
        public string TargetModuleName { get; set; } = nameof(TargetModuleName);

        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractTargetId")]
        public string TargetId { get; set; } = nameof(TargetId);

        /// <summary>
        /// 申請者ユーザー (User モジュールへの Link)。申請時にエンジンが書き込む。
        /// 申請書側の条件は「(フィールド名).Applicant.Value」のリンク越し参照でこれを使う。
        /// </summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractApplicant")]
        public string Applicant { get; set; } = nameof(Applicant);

        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractAttemptNo")]
        public string AttemptNo { get; set; } = nameof(AttemptNo);

        [Designer(Index = 9, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractCurrentStepNo")]
        public string CurrentStepNo { get; set; } = nameof(CurrentStepNo);

        /// <summary>
        /// 承認メンバー一覧 (フローモジュール上の List フィールド。承認メンバーモジュールを Flow で絞る)。
        /// メンバーモジュールはこの一覧の参照先として決まる。申請書側の条件は
        /// 「(フィールド名).Members.～」のリンク越し存在条件でこれを参照する (一覧フィールドの複製は不要)。
        /// </summary>
        [Designer(Index = 10, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractMembers")]
        public string Members { get; set; } = nameof(Members);

        /// <summary>承認履歴一覧 (フローモジュール上の List フィールド)。履歴モジュールはこの一覧の参照先として決まる。</summary>
        [Designer(Index = 11, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFlowContractHistories")]
        public string Histories { get; set; } = nameof(Histories);

        private protected override HashSet<string> RequiredRoleNames => new()
        {
            nameof(Status), nameof(TargetModuleName), nameof(TargetId), nameof(Applicant),
            nameof(AttemptNo), nameof(CurrentStepNo), nameof(Members), nameof(Histories),
        };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            CheckListRole<ApprovalMemberContractFieldDesign>(context, result, nameof(Members), Members);
            CheckListRole<ApprovalHistoryContractFieldDesign>(context, result, nameof(Histories), Histories);
            return result;
        }
    }

    /// <summary>
    /// 承認メンバーモジュールの契約。
    /// 役割はエンジン・UI・申請書側の条件が読むものだけなので全て必須。
    /// 唯一の例外が TurnNotifyMail (通知メールのオプトイン。空 = 通知しない)。
    /// </summary>
    [Designer(DisplayName = "$ApprovalMemberContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalMemberContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalMemberContractFieldDesign() : base(typeof(ApprovalMemberContractFieldDesign).FullName!) { }

        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractFlow")]
        public string Flow { get; set; } = nameof(Flow);

        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractAttemptNo")]
        public string AttemptNo { get; set; } = nameof(AttemptNo);

        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractStepNo")]
        public string StepNo { get; set; } = nameof(StepNo);

        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractStepName")]
        public string StepName { get; set; } = nameof(StepName);

        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractStepType")]
        public string StepType { get; set; } = nameof(StepType);

        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractCompletionPolicy")]
        public string CompletionPolicy { get; set; } = nameof(CompletionPolicy);

        [Designer(Index = 9, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractIsCommentRequiredOnReject")]
        public string IsCommentRequiredOnReject { get; set; } = nameof(IsCommentRequiredOnReject);

        [Designer(Index = 10, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractReturnScope")]
        public string ReturnScope { get; set; } = nameof(ReturnScope);

        [Designer(Index = 11, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractApproverUser")]
        public string ApproverUser { get; set; } = nameof(ApproverUser);

        [Designer(Index = 12, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractIsRequired")]
        public string IsRequired { get; set; } = nameof(IsRequired);

        /// <summary>最終承認ステップのメンバーか (条件式で「最終承認者」を表すためのスナップショット)。</summary>
        [Designer(Index = 13, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractIsFinalStep")]
        public string IsFinalStep { get; set; } = nameof(IsFinalStep);

        [Designer(Index = 14, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractStatus")]
        public string Status { get; set; } = nameof(Status);

        [Designer(Index = 15, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractActedAt")]
        public string ActedAt { get; set; } = nameof(ActedAt);

        /// <summary>
        /// 順番到達通知メール (任意)。自モジュールの MailField 名。
        /// 設定すると、承認の順番が回ってきたメンバーへエンジンが通知メールを送る。空 = 通知しない。
        /// </summary>
        [Designer(Index = 16, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMemberContractTurnNotifyMail")]
        public string TurnNotifyMail { get; set; } = string.Empty;

        //Required = what the engine needs to identify a member and drive the state machine.
        //Optional roles fall into two kinds:
        //  snapshot/display only (StepName / IsFinalStep / ActedAt): empty = not written, UI shows without them
        //  policy (StepType / CompletionPolicy / ReturnScope / IsCommentRequiredOnReject / IsRequired): empty = engine uses the default
        //  (Approval / RequiredMembers / ApplicantOnly / false / true). See ApprovalMemberDefaults.
        private protected override HashSet<string> RequiredRoleNames => new()
        {
            nameof(Flow), nameof(AttemptNo), nameof(StepNo), nameof(ApproverUser), nameof(Status),
        };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //TurnNotifyMail は MailField であること (存在チェックは基底の役割チェックが行う)
            if (!string.IsNullOrEmpty(TurnNotifyMail))
            {
                var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
                var field = ownModule?.Fields.FirstOrDefault(e => e.Name == TurnNotifyMail);
                if (field != null && field is not MailFieldDesign)
                {
                    result.Add(new FieldDesignCheckInfo
                    {
                        Location = new FieldDesignDataLocation
                        { Module = context.OwnerModule, Field = Name, Member = nameof(TurnNotifyMail) },
                        Message = string.Format(Properties.Resources.ApprovalCheck_RoleMustBeMailFieldFormat, TurnNotifyMail),
                    });
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 承認履歴モジュールの契約 (追記のみ)。役割は UI が読むものだけなので全て必須。
    /// </summary>
    [Designer(DisplayName = "$ApprovalHistoryContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalHistoryContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalHistoryContractFieldDesign() : base(typeof(ApprovalHistoryContractFieldDesign).FullName!) { }

        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$ApprovalHistoryContractFlow")]
        public string Flow { get; set; } = nameof(Flow);

        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$ApprovalHistoryContractAttemptNo")]
        public string AttemptNo { get; set; } = nameof(AttemptNo);

        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$ApprovalHistoryContractAction")]
        public string Action { get; set; } = nameof(Action);

        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$ApprovalHistoryContractActorUser")]
        public string ActorUser { get; set; } = nameof(ActorUser);

        [Designer(Index = 10, CandidateType = CandidateType.Field, DisplayName = "$ApprovalHistoryContractComment")]
        public string Comment { get; set; } = nameof(Comment);

        [Designer(Index = 11, CandidateType = CandidateType.Field, DisplayName = "$ApprovalHistoryContractActedAt")]
        public string ActedAt { get; set; } = nameof(ActedAt);

        //The engine only writes history rows; the UI only displays them. Flow is required to find the rows,
        //every other role is optional (empty = not recorded / not shown).
        private protected override HashSet<string> RequiredRoleNames => new() { nameof(Flow) };
    }
}
