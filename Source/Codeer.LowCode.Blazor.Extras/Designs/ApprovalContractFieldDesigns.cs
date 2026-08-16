using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>承認フローモジュールの契約。Members / Histories の一覧の先がメンバー・履歴モジュールになる。</summary>
    [Designer(DisplayName = "$ApprovalFlowContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalFlowContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalFlowContractFieldDesign() : base(typeof(ApprovalFlowContractFieldDesign).FullName!) { }

        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string Status { get; set; } = nameof(Status);

        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string TargetModuleName { get; set; } = nameof(TargetModuleName);

        [Designer(Index = 5, CandidateType = CandidateType.Field)]
        public string TargetId { get; set; } = nameof(TargetId);

        [Designer(Index = 6, CandidateType = CandidateType.Field)]
        public string RouteName { get; set; } = nameof(RouteName);

        /// <summary>
        /// 申請者ユーザー (User モジュールへの Link)。申請時にエンジンが書き込む。
        /// 申請書側の条件は「(フィールド名).Applicant.Value」のリンク越し参照でこれを使う。
        /// </summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field)]
        public string Applicant { get; set; } = nameof(Applicant);

        [Designer(Index = 8, CandidateType = CandidateType.Field)]
        public string AttemptNo { get; set; } = nameof(AttemptNo);

        [Designer(Index = 9, CandidateType = CandidateType.Field)]
        public string CurrentStepNo { get; set; } = nameof(CurrentStepNo);

        /// <summary>
        /// 承認メンバー一覧 (フローモジュール上の List フィールド。承認メンバーモジュールを Flow で絞る)。
        /// メンバーモジュールはこの一覧の参照先として決まる。申請書側の条件は
        /// 「(フィールド名).Members.～」のリンク越し存在条件でこれを参照する (一覧フィールドの複製は不要)。
        /// </summary>
        [Designer(Index = 10, CandidateType = CandidateType.Field)]
        public string Members { get; set; } = nameof(Members);

        /// <summary>承認履歴一覧 (フローモジュール上の List フィールド)。履歴モジュールはこの一覧の参照先として決まる。</summary>
        [Designer(Index = 11, CandidateType = CandidateType.Field)]
        public string Histories { get; set; } = nameof(Histories);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            CheckListRole<ApprovalMemberContractFieldDesign>(context, result, nameof(Members), Members);
            CheckListRole<ApprovalHistoryContractFieldDesign>(context, result, nameof(Histories), Histories);
            return result;
        }
    }

    /// <summary>承認メンバーモジュールの契約。</summary>
    [Designer(DisplayName = "$ApprovalMemberContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalMemberContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalMemberContractFieldDesign() : base(typeof(ApprovalMemberContractFieldDesign).FullName!) { }

        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string Flow { get; set; } = nameof(Flow);

        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string AttemptNo { get; set; } = nameof(AttemptNo);

        [Designer(Index = 5, CandidateType = CandidateType.Field)]
        public string StepNo { get; set; } = nameof(StepNo);

        [Designer(Index = 6, CandidateType = CandidateType.Field)]
        public string StepName { get; set; } = nameof(StepName);

        [Designer(Index = 7, CandidateType = CandidateType.Field)]
        public string StepType { get; set; } = nameof(StepType);

        [Designer(Index = 8, CandidateType = CandidateType.Field)]
        public string CompletionPolicy { get; set; } = nameof(CompletionPolicy);

        [Designer(Index = 9, CandidateType = CandidateType.Field)]
        public string IsCommentRequiredOnReject { get; set; } = nameof(IsCommentRequiredOnReject);

        [Designer(Index = 10, CandidateType = CandidateType.Field)]
        public string ReturnScope { get; set; } = nameof(ReturnScope);

        [Designer(Index = 11, CandidateType = CandidateType.Field)]
        public string ApproverUser { get; set; } = nameof(ApproverUser);

        [Designer(Index = 12, CandidateType = CandidateType.Field)]
        public string IsRequired { get; set; } = nameof(IsRequired);

        /// <summary>最終承認ステップのメンバーか (条件式で「最終承認者」を表すためのスナップショット)。</summary>
        [Designer(Index = 13, CandidateType = CandidateType.Field)]
        public string IsFinalStep { get; set; } = nameof(IsFinalStep);

        [Designer(Index = 14, CandidateType = CandidateType.Field)]
        public string Status { get; set; } = nameof(Status);

        [Designer(Index = 15, CandidateType = CandidateType.Field)]
        public string ActedAt { get; set; } = nameof(ActedAt);

        /// <summary>
        /// 順番到達通知メール (任意)。自モジュールの MailField 名。
        /// 設定すると、承認の順番が回ってきたメンバーへエンジンが通知メールを送る。空 = 通知しない。
        /// </summary>
        [Designer(Index = 16, CandidateType = CandidateType.Field)]
        public string TurnNotifyMail { get; set; } = string.Empty;

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

    /// <summary>承認履歴モジュールの契約。</summary>
    [Designer(DisplayName = "$ApprovalHistoryContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalHistoryContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalHistoryContractFieldDesign() : base(typeof(ApprovalHistoryContractFieldDesign).FullName!) { }

        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string Flow { get; set; } = nameof(Flow);

        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string AttemptNo { get; set; } = nameof(AttemptNo);

        [Designer(Index = 5, CandidateType = CandidateType.Field)]
        public string StepNo { get; set; } = nameof(StepNo);

        [Designer(Index = 6, CandidateType = CandidateType.Field)]
        public string Action { get; set; } = nameof(Action);

        [Designer(Index = 7, CandidateType = CandidateType.Field)]
        public string ActorUser { get; set; } = nameof(ActorUser);

        [Designer(Index = 8, CandidateType = CandidateType.Field)]
        public string FromStatus { get; set; } = nameof(FromStatus);

        [Designer(Index = 9, CandidateType = CandidateType.Field)]
        public string ToStatus { get; set; } = nameof(ToStatus);

        [Designer(Index = 10, CandidateType = CandidateType.Field)]
        public string Comment { get; set; } = nameof(Comment);

        [Designer(Index = 11, CandidateType = CandidateType.Field)]
        public string ActedAt { get; set; } = nameof(ActedAt);
    }

    /// <summary>
    /// 経路マスタ (経路) モジュールの契約。Steps の一覧の先がステップモジュールになる。
    /// 経路マスタはただのユーザー定義モジュールで、エンジンは関与しない
    /// (スクリプトの LoadRoute がこの契約で読む材料。管理画面も通常のローコードで作る)。
    /// </summary>
    [Designer(DisplayName = "$ApprovalRouteContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalRouteContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalRouteContractFieldDesign() : base(typeof(ApprovalRouteContractFieldDesign).FullName!) { }

        /// <summary>経路名 (LoadRoute の引数と照合するキー)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string RouteName { get; set; } = nameof(RouteName);

        /// <summary>ステップ一覧 (経路モジュール上の List フィールド)。ステップモジュールはこの一覧の参照先として決まる。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string Steps { get; set; } = nameof(Steps);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            CheckListRole<ApprovalRouteStepContractFieldDesign>(context, result, nameof(Steps), Steps);
            return result;
        }
    }

    /// <summary>
    /// 経路マスタ (ステップ) モジュールの契約。Members の一覧の先が承認者モジュールになる。
    /// 役割を空にすると「使わない」宣言 (チェック対象外・LoadRoute は既定値に倒す)。
    /// 承認者は2形態: Members (1:N の承認者モジュール = 複数人) か、ApproverUser (ステップ行に直付けの
    /// Link = 1ステップ1人のシンプル構成)。どちらか一方を設定する (両方空はデザインチェックがエラーにする)。
    /// </summary>
    [Designer(DisplayName = "$ApprovalRouteStepContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalRouteStepContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalRouteStepContractFieldDesign() : base(typeof(ApprovalRouteStepContractFieldDesign).FullName!) { }

        /// <summary>経路行への FK (Link)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string Route { get; set; } = nameof(Route);

        /// <summary>ステップの並び順 (数値)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string StepNo { get; set; } = nameof(StepNo);

        [Designer(Index = 5, CandidateType = CandidateType.Field)]
        public string StepName { get; set; } = nameof(StepName);

        /// <summary>Approval / Confirmation (空 = Approval)。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field)]
        public string StepType { get; set; } = nameof(StepType);

        /// <summary>RequiredMembers / All / Any (空 = RequiredMembers)。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field)]
        public string CompletionPolicy { get; set; } = nameof(CompletionPolicy);

        [Designer(Index = 8, CandidateType = CandidateType.Field)]
        public string IsCommentRequiredOnReject { get; set; } = nameof(IsCommentRequiredOnReject);

        /// <summary>ApplicantOnly / AnyPreviousStep (空 = ApplicantOnly)。</summary>
        [Designer(Index = 9, CandidateType = CandidateType.Field)]
        public string ReturnScope { get; set; } = nameof(ReturnScope);

        /// <summary>承認者一覧 (ステップモジュール上の List フィールド)。承認者モジュールはこの一覧の参照先として決まる。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.Field)]
        public string Members { get; set; } = nameof(Members);

        /// <summary>
        /// ステップ行に直付けの承認者 (ユーザーへの Link)。1ステップ1人のシンプル構成用で、
        /// 使う場合は Members を空にしてこちらを設定する (既定は空 = 使わない)。
        /// </summary>
        [Designer(Index = 11, CandidateType = CandidateType.Field)]
        public string ApproverUser { get; set; } = string.Empty;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            if (!string.IsNullOrEmpty(Members))
            {
                CheckListRole<ApprovalRouteStepMemberContractFieldDesign>(context, result, nameof(Members), Members);
            }
            if (string.IsNullOrEmpty(Members) && string.IsNullOrEmpty(ApproverUser))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Members) },
                    Message = Properties.Resources.ApprovalCheck_StepApproverRoleRequired,
                });
            }
            return result;
        }
    }

    /// <summary>経路マスタ (ステップ承認者) モジュールの契約。</summary>
    [Designer(DisplayName = "$ApprovalRouteStepMemberContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalRouteStepMemberContractFieldDesign : ContractFieldDesignBase
    {
        public ApprovalRouteStepMemberContractFieldDesign() : base(typeof(ApprovalRouteStepMemberContractFieldDesign).FullName!) { }

        /// <summary>ステップ行への FK (Link)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field)]
        public string Step { get; set; } = nameof(Step);

        /// <summary>承認者ユーザー (User モジュールへの Link)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field)]
        public string ApproverUser { get; set; } = nameof(ApproverUser);

        /// <summary>必須承認者か (空 = 必須)。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Field)]
        public string IsRequired { get; set; } = nameof(IsRequired);
    }
}
