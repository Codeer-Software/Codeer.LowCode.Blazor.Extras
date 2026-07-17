using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [ToolboxIcon(PackIconMaterialKind = "ShieldKeyOutline")]
    [Designer(DisplayName = "$PasswordHashField")]
    public class PasswordHashFieldDesign() : FieldDesignBase(typeof(PasswordHashFieldDesign).FullName!)
    {
        [Designer(Index = 2, CandidateType = CandidateType.Field, DisplayName = "PasswordField")]
        public string PasswordFieldName { get; set; } = string.Empty;

        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnHash"), DbColumn(nameof(PasswordHashFieldData.Hash), IsWriteOnly = true)]
        public string DbColumnHash { get; set; } = string.Empty;

        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnSalt"), DbColumn(nameof(PasswordHashFieldData.Salt), IsWriteOnly = true)]
        public string DbColumnSalt { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(PasswordHashFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new PasswordHashField(this);
        public override FieldDataBase? CreateData() => new PasswordHashFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnHash), DbColumnHash).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnSalt), DbColumnSalt).AddTo(result);
            return result;
        }
    }
}
