using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public enum GanttViewMode
    {
        Day,
        Week,
        Month
    }

    public class GanttItem
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Progress { get; set; }
        public Module? Module { get; set; }
    }

    public class GanttField(GanttFieldDesign design)
        : FieldBase<GanttFieldDesign>(design), ISearchResultsViewField
    {
        private SearchCondition? _additionalCondition;

        [ScriptHide]
        public Func<SearchCondition?, Task> OnQueryChangedAsync { get; set; } = _ => Task.CompletedTask;

        [ScriptHide]
        public Func<Task> OnSearchDataChangedAsync { get; set; } = () => Task.CompletedTask;

        [ScriptHide]
        public Func<Task> OnDataChangedAsync { get; set; } = () => Task.CompletedTask;

        public bool AllowLoad { get; set; } = true;

        [ScriptHide]
        public string ModuleName => Design?.SearchCondition.ModuleName ?? string.Empty;

        public override bool IsModified => false;

        internal List<GanttItem> Items { get; } = [];

        internal DateTime ViewStart { get; private set; } = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        internal GanttViewMode ViewMode { get; private set; } = GanttViewMode.Week;

        public int Page => 0;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            if (this.IsInLayout()) await ReloadAsync();
        }

        [ScriptName("SetAdditionalCondition")]
        public async Task SetAdditionalConditionAsync(ModuleSearcher searcher)
            => await SetAdditionalConditionAsync(searcher.GetSearchCondition(), 0);

        [ScriptHide]
        public async Task SetAdditionalConditionAsync(SearchCondition condition, int page)
        {
            if (condition.ModuleName != Design.SearchCondition.ModuleName)
                throw LowCodeException.Create("{0} Invalid Module", Design.SearchCondition.ModuleName, condition.ModuleName);
            _additionalCondition = condition;
            await OnQueryChangedAsync(GetSearchCondition());
        }

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task OnExternalFieldChangedAsync(string fieldName)
        {
            if (!this.IsInLayout()) return;
            var searchCondition = GetSearchCondition();
            if (searchCondition.GetFieldVariableConditions().All(e => new VariableName(e.Variable).FieldName.Root != fieldName)) return;
            await ReloadAsync();
        }

        [ScriptHide]
        public override async Task OnChildDataChangedAsync()
        {
            var count = Items.Count;
            foreach (var e in Items.ToList())
            {
                if (e.Module?.IsDeleted == true) Items.Remove(e);
            }
            if (count == Items.Count) return;

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (!AllowLoad) return;

            var (rangeStart, rangeEnd) = GetViewDateRange();

            var startVariable = new VariableName($"{Design.StartField}.Value");
            var endVariable = new VariableName($"{Design.EndField}.Value");
            var condition = new SearchCondition
            {
                ModuleName = Design.SearchCondition.ModuleName,
                Condition = MultiMatchCondition.Or(
                    MultiMatchCondition.And(startVariable.GreaterThanOrEqual(rangeStart), startVariable.LessThan(rangeEnd)),
                    MultiMatchCondition.And(endVariable.GreaterThanOrEqual(rangeStart), endVariable.LessThan(rangeEnd)),
                    MultiMatchCondition.And(startVariable.LessThan(rangeStart), endVariable.GreaterThanOrEqual(rangeEnd))
                )
            };

            await SetAdditionalConditionAsync(condition, 0);

            var items = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.DetailLayoutName);
            Items.Clear();
            Items.AddRange(items.Select(ConvertToGanttItem).Where(e => e.Start != default).OrderBy(e => e.Start).ToList());

            NotifyStateChanged();
        }

        internal (DateTime Start, DateTime End) GetViewDateRange()
        {
            switch (ViewMode)
            {
                case GanttViewMode.Day:
                    return (ViewStart.Date, ViewStart.Date.AddDays(1));
                case GanttViewMode.Month:
                    return (ViewStart.Date, ViewStart.Date.AddDays(42));
                default:
                    return (ViewStart.Date, ViewStart.Date.AddDays(14));
            }
        }

        internal async Task AddAsync(DateTime date)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);
            await mod.AssignRequiredCondition(Design.SearchCondition);

            var start = mod.GetField<DateTimeField>(Design.StartField);
            var end = mod.GetField<DateTimeField>(Design.EndField);
            if (start != null) await start.SetValueAsync(date);
            if (end != null) await end.SetValueAsync(date.AddDays(1));

            if (await mod.ShowDialogAsync("OK", "Cancel") != "OK") return;
            if (!mod.ValidateInput())
            {
                await Services.UIService.NotifyError("Input Error");
                return;
            }
            if (await mod.SubmitAsync() != true) return;

            Items.Add(ConvertToGanttItem(mod));
            SortItems();

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        internal async Task EditAsync(Module? mod)
        {
            if (mod == null) return;
            switch (await mod.ShowDialogAsync("Update", "Delete", "Cancel"))
            {
                case "Update":
                    if (!mod.ValidateInput())
                    {
                        await Services.UIService.NotifyError("Input Error");
                        return;
                    }
                    if (await mod.SubmitAsync() != true) return;

                    var item = Items.FirstOrDefault(e => e.Module?.GetIdText() == mod.GetIdText());
                    if (item != null)
                    {
                        item.Text = mod.GetField<TextField>(Design.TextField)?.Value ?? item.Text;
                        item.Start = mod.GetField<DateTimeField>(Design.StartField)?.Value ?? item.Start;
                        item.End = mod.GetField<DateTimeField>(Design.EndField)?.Value ?? item.End;
                        item.Progress = GetProgressValue(mod);
                    }
                    SortItems();
                    break;
                case "Delete":
                    await mod.DeleteAsync();
                    return;
                default:
                    return;
            }

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        internal async Task UpdateTaskDatesAsync(GanttItem item, DateTime newStart, DateTime newEnd)
        {
            if (item.Module == null) return;

            var startField = item.Module.GetField<DateTimeField>(Design.StartField);
            var endField = item.Module.GetField<DateTimeField>(Design.EndField);
            if (startField != null) await startField.SetValueAsync(newStart);
            if (endField != null) await endField.SetValueAsync(newEnd);

            if (await item.Module.SubmitAsync() != true) return;

            item.Start = newStart;
            item.End = newEnd;

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        internal async Task SetViewStartAsync(DateTime date)
        {
            ViewStart = date;
            NotifyStateChanged();
            await ReloadAsync();
        }

        internal async Task SetViewModeAsync(GanttViewMode mode)
        {
            ViewMode = mode;
            NotifyStateChanged();
            await ReloadAsync();
        }

        private async Task InvokeOnDataChangedAsync()
        {
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
        }

        private void SortItems()
        {
            var sorted = Items.OrderBy(e => e.Start).ToList();
            Items.Clear();
            Items.AddRange(sorted);
        }

        private GanttItem ConvertToGanttItem(Module data)
        {
            var text = data.GetField<TextField>(Design.TextField)?.Value;
            var start = data.GetField<DateTimeField>(Design.StartField)?.Value;
            var end = data.GetField<DateTimeField>(Design.EndField)?.Value;

            return new GanttItem
            {
                Module = data,
                Id = data.GetIdText() ?? string.Empty,
                Text = text ?? string.Empty,
                Start = start ?? default,
                End = end ?? start ?? default,
                Progress = GetProgressValue(data),
            };
        }

        private int GetProgressValue(Module data)
        {
            if (string.IsNullOrEmpty(Design.ProgressField)) return 0;
            var val = data.GetField<NumberField>(Design.ProgressField)?.Value;
            return int.TryParse(val?.ToString() ?? string.Empty, out var p) ? Math.Clamp(p, 0, 100) : 0;
        }

        private SearchCondition GetSearchCondition()
            => Design.SearchCondition.MergeSearchCondition(_additionalCondition);
    }
}
