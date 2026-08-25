using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// 承認契約フィールドの解決。承認モジュール群 (フロー / メンバー / 履歴) は
    /// 自分のモジュールに置かれた契約フィールドで「役割→フィールド名」を宣言する。
    /// メンバー・履歴モジュールは、フロー契約の Members / Histories 一覧の参照先として決まる。
    /// </summary>
    internal static class ApprovalContracts
    {
        internal static ApprovalFlowContractFieldDesign? Flow(ModuleDesign? flowModule)
            => flowModule?.Fields.OfType<ApprovalFlowContractFieldDesign>().FirstOrDefault();

        internal static ApprovalMemberContractFieldDesign? Member(ModuleDesign? memberModule)
            => memberModule?.Fields.OfType<ApprovalMemberContractFieldDesign>().FirstOrDefault();

        internal static ApprovalHistoryContractFieldDesign? History(ModuleDesign? historyModule)
            => historyModule?.Fields.OfType<ApprovalHistoryContractFieldDesign>().FirstOrDefault();

        //フロー契約の一覧役割の参照先モジュール (メンバー / 履歴モジュールの解決)
        internal static ModuleDesign? FindListRoleModule(DesignData designData, ModuleDesign flowModule, string listFieldName)
        {
            var moduleName = (flowModule.Fields.FirstOrDefault(e => e.Name == listFieldName) as IListFieldDesign)
                ?.SearchCondition.ModuleName;
            return string.IsNullOrEmpty(moduleName) ? null : designData.Modules.Find(moduleName);
        }
    }

    /// <summary>
    /// 承認モジュール群 (フロー / メンバー / 履歴) とその契約の解決結果。
    /// クライアント (ApprovalFlowField) とサーバー (ApprovalEngine) が同じ解決を使う。
    /// </summary>
    internal sealed class ApprovalModules
    {
        public ModuleDesign FlowModule { get; private init; } = null!;
        public ModuleDesign MemberModule { get; private init; } = null!;
        public ModuleDesign HistoryModule { get; private init; } = null!;

        //契約 (役割→フィールド名のマッピング)。フィールド名はこの解決経由で読む
        public ApprovalFlowContractFieldDesign Flow { get; private init; } = null!;
        public ApprovalMemberContractFieldDesign Member { get; private init; } = null!;
        public ApprovalHistoryContractFieldDesign History { get; private init; } = null!;

        /// <summary>
        /// フローモジュール名から 3 モジュールと契約を解決する。
        /// モジュール・契約のどれかが欠けていれば null (= デザイン不備。デザインチェックが指摘する)。
        /// </summary>
        internal static ApprovalModules? Resolve(DesignData designData, string flowModuleName)
        {
            var flowModule = designData.Modules.Find(flowModuleName);
            var flow = ApprovalContracts.Flow(flowModule);
            if (flowModule == null || flow == null) return null;

            var memberModule = ApprovalContracts.FindListRoleModule(designData, flowModule, flow.Members);
            var historyModule = ApprovalContracts.FindListRoleModule(designData, flowModule, flow.Histories);
            var member = ApprovalContracts.Member(memberModule);
            var history = ApprovalContracts.History(historyModule);
            if (memberModule == null || member == null || historyModule == null || history == null) return null;

            return new ApprovalModules
            {
                FlowModule = flowModule, MemberModule = memberModule, HistoryModule = historyModule,
                Flow = flow, Member = member, History = history,
            };
        }
    }
}
