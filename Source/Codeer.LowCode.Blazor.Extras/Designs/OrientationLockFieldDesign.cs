using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [Designer(DisplayName = "$OrientationLockField")]
    public class OrientationLockFieldDesign() : FieldDesignBase(typeof(OrientationLockFieldDesign).FullName!)
    {
        public enum OrientationKind
        {
            Landscape,
            Portrait,
        }

        [Designer]
        public OrientationKind AllowedOrientation { get; set; } = OrientationKind.Landscape;

        [Designer]
        public string Message { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(OrientationLockFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new OrientationLockField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            return result;
        }
    }
}
