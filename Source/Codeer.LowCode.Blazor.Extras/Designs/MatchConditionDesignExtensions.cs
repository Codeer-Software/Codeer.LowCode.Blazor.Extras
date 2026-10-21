using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>デザインチェックで条件の有無を見るときの判定 (本体が条件を保存前に刈る規則と同じ = 値が未設定の比較・変数が空の比較・子の無い複合条件は無いものとして扱う)。</summary>
    internal static class MatchConditionDesignExtensions
    {
        internal static bool HasEffectiveCondition(this ModuleMatchCondition? condition)
            => condition?.Condition.HasEffectiveCondition() == true;

        internal static bool HasEffectiveCondition(this MatchConditionBase? condition)
            => condition switch
            {
                null => false,
                MultiMatchCondition multi => multi.Children.Any(HasEffectiveCondition),
                FieldValueMatchConditionNonNull fieldValue => fieldValue.Value is not NullValue,
                FieldVariableMatchCondition fieldVariable => !string.IsNullOrEmpty(fieldVariable.Variable),
                _ => true,
            };

        /// <summary>行データ (CurrentUser 以外) を参照する条件があるか。行を伴わない判定 (ジョブの実行可否・検索の静的判定) ではそういう条件は適用されない。</summary>
        internal static bool ReferencesRowData(this MatchConditionBase? condition)
            => condition is IFieldMatchCondition field && !field.SearchTargetVariable.StartsWith(SystemFieldNames.CurrentUser + ".")
                || condition is MultiMatchCondition multi && multi.Children.Any(ReferencesRowData);

        /// <summary>PermissionField の対象にそのフィールドが入るか (除外モードは「指定した名前以外のすべて」で、本体と同じく Id / 楽観ロックは常に対象外)。</summary>
        internal static bool Targets(this PermissionFieldDesign rule, string fieldName)
            => rule.IsExcludeMode
                ? !rule.TargetFields.Contains(fieldName) && fieldName != SystemFieldNames.Id && fieldName != SystemFieldNames.OptimisticLocking
                : rule.TargetFields.Contains(fieldName);
    }
}
