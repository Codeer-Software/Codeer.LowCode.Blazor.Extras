using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    public class MarkerListFieldDesign() : FieldDesignBase(typeof(MarkerListFieldDesign).FullName!),
        ISearchResultsViewFieldDesign
    {
        [Designer(CandidateType = CandidateType.Resource)]
        public string ResourcePath { get; set; } = string.Empty;

        [Designer(Scope = DesignerScope.All)]
        public SearchCondition SearchCondition { get; set; } = new();

        [Designer(CandidateType = CandidateType.DetailLayout)]
        [Layout(ModuleNameMember = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        public string DetailLayoutName { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(NumberFieldDesign)])]
        public string XField { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(NumberFieldDesign)])]
        public string YField { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(TextFieldDesign)])]
        public string LabelField { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.ScriptEvent)]
        public string OnDataChanged { get; set; } = string.Empty;

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
            var result = new List<DesignCheckInfo>();
            context.CheckFieldRelativeFieldExistence(Name, nameof(XField), SearchCondition.ModuleName, XField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(YField), SearchCondition.ModuleName, YField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(LabelField), SearchCondition.ModuleName, LabelField).AddTo(result);
            context.CheckFieldRelativeModuleLayoutExistence(Name, nameof(DetailLayoutName), SearchCondition.ModuleName, DetailLayoutName).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged, null).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnClickMarker), OnClickMarker, null).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDoubleClickPoint), OnDoubleClickPoint, null).AddTo(result);
            result.AddRange(SearchCondition.CheckDesign(context, Name, nameof(SearchCondition)));
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(SearchCondition.ModuleName, XField, x => XField = x)
            .AddField(SearchCondition.ModuleName, YField, x => YField = x)
            .AddField(SearchCondition.ModuleName, LabelField, x => LabelField = x)
            .AddLayout(SearchCondition.ModuleName, ModuleLayoutType.Detail, DetailLayoutName, x => DetailLayoutName = x)
            .AddMatchCondition(SearchCondition)
            .Build();
    }
}
