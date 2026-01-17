using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    public class MarkerListFieldDesign : FieldDesignBase, IDataDependentField
    {
        public MarkerListFieldDesign() : base(typeof(MarkerListFieldDesign).FullName!) { }

        [Designer(CandidateType = CandidateType.Resource)]
        public string ResourcePath { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [TargetFieldType(Types = [typeof(ListFieldDesignBase)])]
        public string ListField { get; set; } = string.Empty;

        [Designer]
        public string LabelFieldOfListField { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.ScriptEvent),
         ScriptMethod(ArgumentTypes = ["string"], ArgumentNames = ["id"])]
        public string OnClickMarker { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.ScriptEvent),
         ScriptMethod(ArgumentTypes = ["int", "int"], ArgumentNames = ["x", "y"])]
        public string OnDoubleClickPoint { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(MarkerListFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new MarkerListField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldFieldExistence(Name, nameof(ListField), ListField).AddTo(result);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(ListField, x => ListField = x)
            .Build();

        public List<string> GetDependencyFields() => string.IsNullOrEmpty(ListField) ? new() : [ListField];
    }
}
