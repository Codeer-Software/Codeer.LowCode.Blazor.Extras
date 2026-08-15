using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// 経路マスタ行 (経路 / ステップ / ステップ承認者) から経路の中間表現 (ApprovalRouteData) を組み立てる。
    /// マスタはただのユーザー定義モジュールで、フィールド名は契約で解決する (空の役割 = 使わない)。
    /// 承認者は Members (1:N 複数人) とステップ直付け ApproverUser (1ステップ1人) の2形態。
    /// 空値はスクリプト組み立てと同じ既定 (StepType=Approval / CompletionPolicy=RequiredMembers /
    /// ReturnScope=ApplicantOnly / IsCommentRequiredOnReject=true / IsRequired=true) に倒す。
    /// </summary>
    internal static class ApprovalRouteMasterLogic
    {
        internal static ApprovalRouteData Build(string routeName,
            ApprovalRouteStepContractFieldDesign stepNames,
            ApprovalRouteStepMemberContractFieldDesign memberNames,
            List<ModuleData> stepRows, List<ModuleData> memberRows)
        {
            var route = new ApprovalRouteData { Name = routeName };
            foreach (var stepRow in stepRows.OrderBy(e => GetDecimal(e, stepNames.StepNo) ?? decimal.MaxValue))
            {
                var step = route.AddStep(GetString(stepRow, stepNames.StepName) ?? string.Empty);

                var stepType = GetString(stepRow, stepNames.StepType);
                if (!string.IsNullOrEmpty(stepType)) step.StepType = stepType;
                var policy = GetString(stepRow, stepNames.CompletionPolicy);
                if (!string.IsNullOrEmpty(policy)) step.CompletionPolicy = policy;
                var returnScope = GetString(stepRow, stepNames.ReturnScope);
                if (!string.IsNullOrEmpty(returnScope)) step.ReturnScope = returnScope;
                var commentRequired = GetBool(stepRow, stepNames.IsCommentRequiredOnReject);
                if (commentRequired != null) step.IsCommentRequiredOnReject = commentRequired.Value;

                //ステップ直付けの承認者 (シンプル構成)。承認者未選択はスキップ (マスタの編集途中を許容する)
                if (!string.IsNullOrEmpty(stepNames.ApproverUser))
                {
                    var user = GetLinkValue(stepRow, stepNames.ApproverUser);
                    if (!string.IsNullOrEmpty(user)) step.AddMember(user, true);
                }

                var stepId = GetId(stepRow);
                foreach (var memberRow in memberRows.Where(e => GetLinkValue(e, memberNames.Step) == stepId))
                {
                    var user = GetLinkValue(memberRow, memberNames.ApproverUser);
                    if (string.IsNullOrEmpty(user)) continue;
                    step.AddMember(user, GetBool(memberRow, memberNames.IsRequired) ?? true);
                }
            }
            return route;
        }

        internal static string? GetId(ModuleData data)
            => (data.Fields.GetValueOrDefault(Codeer.LowCode.Blazor.DesignLogic.SystemFieldNames.Id) as ValueFieldDataBase<string>)?.Value;

        static string? GetString(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value;

        static string? GetLinkValue(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as LinkFieldData)?.Value;

        static decimal? GetDecimal(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as NumberFieldData)?.Value;

        static bool? GetBool(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as BooleanFieldData)?.Value;
    }
}
