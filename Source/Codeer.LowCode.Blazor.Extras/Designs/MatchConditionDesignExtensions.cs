using Codeer.LowCode.Blazor.Repository;
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
    }
}
