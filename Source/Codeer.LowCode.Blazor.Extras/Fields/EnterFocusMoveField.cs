using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
#pragma warning disable CS0612 // 非推奨: フォーカス制御機能 (FocusControlMode) で代替。既存デザインのため動作は維持する
    public class EnterFocusMoveField(EnterFocusMoveFieldDesign design)
        : FieldBase<EnterFocusMoveFieldDesign>(design)
    {
        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
    }
#pragma warning restore CS0612
}
