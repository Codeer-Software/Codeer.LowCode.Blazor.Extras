using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class PasswordHashField(PasswordHashFieldDesign design) : FieldBase<PasswordHashFieldDesign>(design)
    {
        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
    }
}
