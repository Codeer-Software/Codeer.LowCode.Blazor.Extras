using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// 承認契約フィールドの解決。承認モジュール群 (フロー / メンバー / 履歴) は
    /// 自分のモジュールに置かれた契約フィールドで「役割→フィールド名」を宣言する。
    /// メンバー・履歴モジュールは、フロー契約の Members / Histories 一覧の参照先として決まる。
    /// </summary>
    public static class ApprovalContracts
    {
        public static ApprovalFlowContractFieldDesign? Flow(ModuleDesign? flowModule)
            => flowModule?.Fields.OfType<ApprovalFlowContractFieldDesign>().FirstOrDefault();

        public static ApprovalMemberContractFieldDesign? Member(ModuleDesign? memberModule)
            => memberModule?.Fields.OfType<ApprovalMemberContractFieldDesign>().FirstOrDefault();

        public static ApprovalHistoryContractFieldDesign? History(ModuleDesign? historyModule)
            => historyModule?.Fields.OfType<ApprovalHistoryContractFieldDesign>().FirstOrDefault();

        /// <summary>フロー契約の一覧役割の参照先モジュール名 (メンバー / 履歴モジュールの解決)。</summary>
        public static string GetListRoleModuleName(ModuleDesign? flowModule, string listFieldName)
            => (flowModule?.Fields.FirstOrDefault(e => e.Name == listFieldName) as IListFieldDesign)
                ?.SearchCondition.ModuleName ?? string.Empty;

        public static string GetMemberModuleName(ModuleDesign? flowModule)
            => GetListRoleModuleName(flowModule, Flow(flowModule)?.Members ?? string.Empty);

        public static string GetHistoryModuleName(ModuleDesign? flowModule)
            => GetListRoleModuleName(flowModule, Flow(flowModule)?.Histories ?? string.Empty);

    }
}
