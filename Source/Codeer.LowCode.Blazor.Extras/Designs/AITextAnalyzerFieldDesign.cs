using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [ToolboxIcon(PackIconMaterialKind = "HeadSnowflakeOutline")]
    [Designer(DisplayName = "$AITextAnalyzerField")]
    public class AITextAnalyzerFieldDesign() : FieldDesignBase(typeof(AITextAnalyzerFieldDesign).FullName!)
    {
        [Designer(CandidateType = CandidateType.MultilineString)]
        public string Remarks { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.ScriptEvent)]
        public string DataImportCompleted { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [TargetFieldType(Types = [typeof(FileFieldDesign)])]
        public string FileField { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(AITextAnalyzerFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new AITextAnalyzerField(this);
        public override FieldDataBase? CreateData() => null;
    }
}
