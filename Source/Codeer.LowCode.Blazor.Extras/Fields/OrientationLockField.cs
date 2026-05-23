using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class OrientationLockField(OrientationLockFieldDesign design) : FieldBase<OrientationLockFieldDesign>(design)
    {
        public override bool IsModified => false;
        public override FieldDataBase? GetData() => null;
        public override FieldSubmitData GetSubmitData() => new();
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
    }
}
