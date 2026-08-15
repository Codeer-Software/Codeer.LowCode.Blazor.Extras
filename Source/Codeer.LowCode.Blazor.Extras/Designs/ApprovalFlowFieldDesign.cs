using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 承認フローフィールド。申請書モジュールに1つ置くと、申請・承認・却下・差し戻し・
    /// 取り下げ・再申請・回覧確認と進捗表示・履歴表示を提供する。
    /// 状態遷移はサーバーの command API だけが行い、承認データはユーザー定義の
    /// 承認モジュール (フロー / メンバー / 履歴) に保存される。
    /// 指定するのはフローモジュールだけで、フィールド名の解決は各モジュールに置いた
    /// 契約フィールド (ApprovalXxxContractFieldDesign) の役割マッピング経由。
    /// メンバー・履歴モジュールはフロー契約の Members / Histories 一覧の参照先として決まる。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    [Designer(DisplayName = "$ApprovalFlowField")]
    public class ApprovalFlowFieldDesign : FieldDesignBase
    {
        public ApprovalFlowFieldDesign() : base(typeof(ApprovalFlowFieldDesign).FullName!) { }

        /// <summary>承認フロー行への FK 列。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "$DbColumn"),
         DbColumn(nameof(ApprovalFlowFieldData.Id)), Join,
         ModuleMember(Member = nameof(FlowModuleName))]
        public string DbColumn { get; set; } = string.Empty;

        [Designer(Index = 6, CandidateType = CandidateType.Module, DisplayName = "$ApprovalFlowModuleName")]
        public string FlowModuleName { get; set; } = "ApprovalFlow";

        /// <summary>
        /// 取り下げの許可範囲 (業務ポリシー)。既定は「承認が始まる前のみ」。
        /// エンジンが強制するのは資格・版・遷移の整合だけで、この種の業務ポリシーはデザインで選ぶ。
        /// </summary>
        [Designer(Index = 11, DisplayName = "$ApprovalWithdrawPolicy")]
        public ApprovalWithdrawPolicy WithdrawPolicy { get; set; } = ApprovalWithdrawPolicy.BeforeFirstApproval;

        [Designer(Index = 12, DisplayName = "$ApprovalShowProgress")]
        public bool ShowProgress { get; set; } = true;

        [Designer(Index = 13, DisplayName = "$ApprovalShowHistory")]
        public bool ShowHistory { get; set; } = true;

        [Designer(Index = 14, DisplayName = "$ApprovalShowComment")]
        public bool ShowComment { get; set; } = true;

        /// <summary>
        /// 組み込みのアクションボタン群を表示するか。
        /// false にすると標準 UI を退かせて、ButtonField ＋ スクリプト API で
        /// アプリ独自の承認 UI を作れる (サーバーの検証はどの UI からでも同じ)。
        /// </summary>
        [Designer(Index = 15, DisplayName = "$ApprovalShowActions")]
        public bool ShowActions { get; set; } = true;

        /// <summary>
        /// 経路を組み立てるスクリプト (ApprovalRouteData を返す。null で申請中止)。
        /// 設定すると組み込みの申請・再申請ボタンが表示され、スクリプト API の Submit() / Resubmit() も使える。
        /// </summary>
        [Designer(Index = 18, CandidateType = CandidateType.ScriptEvent, DisplayName = "$ApprovalOnBuildRoute")]
        public string OnBuildRoute { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(ApprovalFlowFieldComponent).FullName!;

        //条件はフロー行へのリンク越し参照で書く: 状態 = "(フィールド名).Status.Value"(未申請 = null)、
        //申請者 = "(フィールド名).Applicant.Value"、承認待ち等 = "(フィールド名).Members.～" の存在条件。
        //汎用の条件エディタがリンク越しパスを対象候補に列挙するため、専用の検索UI・検索コントロールは持たない
        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new ApprovalFlowField(this);

        public override FieldDataBase? CreateData() => new ApprovalFlowFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnBuildRoute), OnBuildRoute,
                context.GetScriptMethodAttribute(GetType(), nameof(OnBuildRoute))).AddTo(result);
            context.CheckFieldModuleExistence(Name, nameof(FlowModuleName), FlowModuleName).AddTo(result);

            //フローモジュールが契約 (役割マッピング) を実装していること。
            //役割ごとのフィールド存在・メンバー / 履歴モジュールの契約は各契約フィールドの CheckDesign が検証する
            var flowModule = context.DesignData.Modules.Find(FlowModuleName);
            if (flowModule != null && ApprovalContracts.Flow(flowModule) == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    {
                        Module = context.OwnerModule,
                        Field = Name,
                        Member = nameof(FlowModuleName),
                    },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                        FlowModuleName, nameof(ApprovalFlowContractFieldDesign)),
                });
            }

            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddModule(FlowModuleName, x => FlowModuleName = x)
            .Build();
    }
}
