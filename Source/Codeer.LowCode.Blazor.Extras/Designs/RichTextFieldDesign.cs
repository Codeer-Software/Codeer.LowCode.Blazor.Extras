using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    public class RichTextFieldDesign() : ValueFieldDesignBase(typeof(RichTextFieldDesign).FullName!)
    {
        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$RichTextFieldDbColumn"), DbColumn(nameof(RichTextFieldData.Value))]
        public string DbColumn { get; set; } = string.Empty;

        [Designer(Index = 1, DisplayName = "$RichTextFieldHeight")]
        public string Height { get; set; } = "200px";

        public override string GetWebComponentTypeFullName() => typeof(RichTextFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new RichTextField(this);

        public override FieldDataBase? CreateData() => new RichTextFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnDataChanged))).AddTo(result);
            return result;
        }
    }
}
