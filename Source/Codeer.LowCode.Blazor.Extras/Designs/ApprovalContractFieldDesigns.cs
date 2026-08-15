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
    /// 承認契約フィールドの基底。承認モジュール上に置き、「役割 → 自モジュールのフィールド名」の
    /// マッピングを宣言する (役割プロパティ名 = 既定フィールド名 = 初期値。nameof で自己参照)。
    /// エンジン・チェック・生成はこのマッピング経由で
    /// フィールドを解決するため、承認モジュールのフィールド名は自由に変えられる
    /// (役割プロパティは自モジュールのフィールド参照としてリネームにも追従する)。
    /// 契約フィールドを置いたモジュールに役割のフィールドが無ければデザインチェックがエラーにする。
    /// UI もデータも持たない設定運搬フィールド (PermissionField と同じ流儀)。
    /// </summary>
    public abstract class ApprovalContractFieldDesignBase : FieldDesignBase
    {
        protected ApprovalContractFieldDesignBase(string typeFullName) : base(typeFullName) { }

        public override string GetWebComponentTypeFullName() => string.Empty;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new ApprovalContractField(this);

        /// <summary>役割プロパティ (プロパティ名 = 役割名) の一覧。チェックとリネーム追従で使う。</summary>
        internal IEnumerable<System.Reflection.PropertyInfo> GetRoleProperties()
            => GetType().GetProperties()
                .Where(e => e.DeclaringType != typeof(FieldDesignBase) && e.PropertyType == typeof(string) &&
                            e.GetCustomAttributes(typeof(DesignerAttribute), true).Length > 0);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //同じ契約フィールドが複数あると解決が曖昧になる
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null && ownModule.Fields.Count(e => e.GetType() == GetType()) > 1)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldDuplicatedFormat,
                        GetType().Name),
                });
            }

            //役割のフィールドが自モジュールに存在すること (=このモジュールが契約を実装していること)
            foreach (var role in GetRoleProperties())
            {
                var fieldName = role.GetValue(this)?.ToString() ?? string.Empty;
                context.CheckFieldFieldExistence(Name, role.Name, fieldName).AddTo(result);
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context));
            foreach (var role in GetRoleProperties())
            {
                var current = role.GetValue(this)?.ToString() ?? string.Empty;
                builder.AddField(current, x => role.SetValue(this, x));
            }
            return builder.Build();
        }

        //役割が一覧フィールドであること + 一覧の先のモジュールが指定の契約を実装していることのチェック
        private protected void CheckListRole<TContract>(DesignCheckContext context, List<DesignCheckInfo> result,
            string roleName, string fieldName) where TContract : ApprovalContractFieldDesignBase
        {
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            var field = ownModule?.Fields.FirstOrDefault(e => e.Name == fieldName);
            if (field == null) return; //不在は役割チェックが指摘済み

            if (field is not IListFieldDesign list)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = roleName },
                    Message = string.Format(Properties.Resources.ApprovalCheck_RoleMustBeListFormat, fieldName),
                });
                return;
            }

            var targetModule = context.DesignData.Modules.Find(list.SearchCondition.ModuleName);
            if (targetModule == null) return; //一覧側のモジュール不在チェックが指摘する
            if (!targetModule.Fields.OfType<TContract>().Any())
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = roleName },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                        targetModule.Name, typeof(TContract).Name),
                });
            }
        }
    }

    /// <summary>承認フローモジュールの契約。Members / Histories の一覧の先がメンバー・履歴モジュールになる。</summary>
    [Designer(DisplayName = "$ApprovalFlowContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalFlowContractFieldDesign : ApprovalContractFieldDesignBase
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
    public class ApprovalMemberContractFieldDesign : ApprovalContractFieldDesignBase
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
    }

    /// <summary>承認履歴モジュールの契約。</summary>
    [Designer(DisplayName = "$ApprovalHistoryContractField")]
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    public class ApprovalHistoryContractFieldDesign : ApprovalContractFieldDesignBase
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
}
