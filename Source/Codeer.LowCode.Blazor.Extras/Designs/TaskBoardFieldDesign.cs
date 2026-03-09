using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Models;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    public class TaskBoardStatusDesign
    {
        public string DisplayText { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Color { get; set; } = string.Empty;

        public bool CanAdd { get; set; } = true;
    }

    public class TaskBoardFieldDesign() : FieldDesignBase(typeof(TaskBoardFieldDesign).FullName!), IDisplayName,
        ISearchResultsViewFieldDesign
    {
        [Designer]
        public string DisplayName { get; set; } = string.Empty;

        [Designer(Scope = DesignerScope.All)]
        public SearchCondition SearchCondition { get; set; } = new();

        [Designer]
        public TaskBoardStatuses Statuses { get; set; } = new();

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(SelectFieldDesign), typeof(TextFieldDesign)])]
        public string StatusField { get; set; } = "";

        [Designer(CandidateType = CandidateType.DetailLayout)]
        [Layout(ModuleNameMember = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        public string DetailLayoutName { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [ModuleMember(Member = $"{nameof(SearchCondition)}.{nameof(SearchCondition.ModuleName)}")]
        [TargetFieldType(Types = [typeof(NumberFieldDesign)])]
        public string SortIndexField { get; set; } = "";

        [Designer(CandidateType = CandidateType.ScriptEvent)]
        public string OnDataChanged { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(TaskBoardFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new TaskBoardField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldRelativeFieldExistence(Name, nameof(StatusField), SearchCondition.ModuleName, StatusField).AddTo(result);
            context.CheckFieldRelativeFieldExistence(Name, nameof(SortIndexField), SearchCondition.ModuleName, SortIndexField).AddTo(result);
            context.CheckFieldRelativeModuleLayoutExistence(Name, nameof(DetailLayoutName), SearchCondition.ModuleName, DetailLayoutName).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged, null).AddTo(result);
            result.AddRange(SearchCondition.CheckDesign(context, Name, nameof(SearchCondition)));
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(SearchCondition.ModuleName, StatusField, value => StatusField = value)
            .AddField(SearchCondition.ModuleName, SortIndexField, value => SortIndexField = value)
            .AddLayout(SearchCondition.ModuleName, ModuleLayoutType.Detail, DetailLayoutName,
                value => DetailLayoutName = value)
            .AddMatchCondition(SearchCondition)
            .Build();
    }
}
