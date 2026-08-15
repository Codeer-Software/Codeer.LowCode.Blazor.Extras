using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 承認契約フィールドのランタイム (何もしない)。
    /// 契約フィールドはデザイン情報 (役割→フィールド名のマッピング) を運ぶだけで、
    /// UI もデータも持たない (PermissionField と同じ流儀)。
    /// </summary>
    public class ApprovalContractField(FieldDesignBase design) : FieldBase<FieldDesignBase>(design)
    {
        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;
    }
}
