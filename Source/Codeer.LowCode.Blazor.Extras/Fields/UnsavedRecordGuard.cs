using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 自分のレコードの Id で子レコードを検索するフィールドの、未保存 (新規) レコード上でのガード。
    /// 新規レコードの Id は仮 Id なので、そのまま検索すると DB の数値 FK 列などで変換エラーになる。
    /// 本体の一覧フィールドと同じく「未保存の親に紐づく子は無い」として読みに行かない。
    /// </summary>
    internal static class UnsavedRecordGuard
    {
        internal static bool IsBoundToUnsavedRecord(this FieldBase field, SearchCondition condition)
            => field.Module.IsNewData
                && condition.GetFieldVariableConditions().Any(e => new VariableName(e.Variable).FieldName.Root == SystemFieldNames.Id);
    }
}
