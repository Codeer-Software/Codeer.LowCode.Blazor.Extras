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
    public class GanttFieldDesign() : FieldDesignBase(typeof(GanttFieldDesign).FullName!), IDisplayName,
        ISearchResultsViewFieldDesign
    {
        [Designer]
        public string DisplayName { get; set; } = string.Empty;

        [Designer(Scope = DesignerScope.All)]
        public SearchCondition SearchCondition { get; set; } = new();

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(TextFieldDesign)])]
        public string TextField { get; set; } = "";

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(DateTimeFieldDesign), typeof(DateFieldDesign)])]
        public string StartField { get; set; } = "";

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(DateTimeFieldDesign), typeof(DateFieldDesign)])]
        public string EndField { get; set; } = "";

        [Designer(CandidateType = CandidateType.Field, Category = nameof(SearchCondition))]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(NumberFieldDesign)])]
        public string ProgressField { get; set; } = "";

        [Designer(CandidateType = CandidateType.Field, Category = nameof(SearchCondition))]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(IdFieldDesign)])]
        public string IdField { get; set; } = "";

        [Designer(CandidateType = CandidateType.Field, Category = nameof(SearchCondition))]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(NumberFieldDesign)])]
        public string ProcessingCounterField { get; set; } = "";

        [Designer(CandidateType = CandidateType.DetailLayout)]
        [Layout(ModuleNameMember = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        public string DetailLayoutName { get; set; } = string.Empty;

        [Designer(Scope = DesignerScope.All, Category = nameof(DependenciesModule))]
        public SearchCondition DependenciesModule { get; set; } = new();

        [Designer(CandidateType = CandidateType.Field, Category = nameof(DependenciesModule))]
        [ModuleMember(Member = $"{nameof(DependenciesModule)}.{nameof(DependenciesModule.ModuleName)}")]
        [TargetFieldType(Types = [typeof(IdFieldDesign), typeof(LinkFieldDesign)])]
        public string DependencySourceIdField { get; set; } = "";

        [Designer(CandidateType = CandidateType.Field, Category = nameof(DependenciesModule))]
        [ModuleMember(Member = $"{nameof(DependenciesModule)}.{nameof(DependenciesModule.ModuleName)}")]
        [TargetFieldType(Types = [typeof(IdFieldDesign), typeof(LinkFieldDesign)])]
        public string DependencyDestinationIdField { get; set; } = "";

        [Designer]
        public bool EnableDayView { get; set; } = true;

        [Designer]
        public bool EnableWeekView { get; set; } = true;

        [Designer]
        public bool EnableMonthView { get; set; } = true;

        [Designer(CandidateType = CandidateType.ScriptEvent)]
        public string OnDataChanged { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(GanttFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new GanttField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldRelativeFieldExistence(Name, nameof(TextField), SearchCondition.ModuleName, TextField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(StartField), SearchCondition.ModuleName, StartField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(EndField), SearchCondition.ModuleName, EndField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(ProgressField), SearchCondition.ModuleName, ProgressField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(IdField), SearchCondition.ModuleName, IdField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(ProcessingCounterField), SearchCondition.ModuleName, ProcessingCounterField).AddTo(result);
            context.CheckFieldRelativeModuleLayoutExistence(Name, nameof(DetailLayoutName), SearchCondition.ModuleName, DetailLayoutName).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(DependencySourceIdField), DependenciesModule.ModuleName, DependencySourceIdField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(DependencyDestinationIdField), DependenciesModule.ModuleName, DependencyDestinationIdField).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged, null).AddTo(result);
            result.AddRange(SearchCondition.CheckDesign(context, Name, nameof(SearchCondition)));
            result.AddRange(DependenciesModule.CheckDesign(context, Name, nameof(DependenciesModule)));
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(SearchCondition.ModuleName, TextField, value => TextField = value)
            .AddField(SearchCondition.ModuleName, StartField, value => StartField = value)
            .AddField(SearchCondition.ModuleName, EndField, value => EndField = value)
            .AddField(SearchCondition.ModuleName, ProgressField, value => ProgressField = value)
            .AddField(SearchCondition.ModuleName, IdField, value => IdField = value)
            .AddField(SearchCondition.ModuleName, ProcessingCounterField, value => ProcessingCounterField = value)
            .AddLayout(SearchCondition.ModuleName, ModuleLayoutType.Detail, DetailLayoutName,
                value => DetailLayoutName = value)
            .AddField(DependenciesModule.ModuleName, DependencySourceIdField, value => DependencySourceIdField = value)
            .AddField(DependenciesModule.ModuleName, DependencyDestinationIdField, value => DependencyDestinationIdField = value)
            .AddMatchCondition(SearchCondition)
            .AddMatchCondition(DependenciesModule)
            .Build();
    }
}
