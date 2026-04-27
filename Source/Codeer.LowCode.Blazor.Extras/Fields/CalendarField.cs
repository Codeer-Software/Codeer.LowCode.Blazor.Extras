using Codeer.LowCode.Blazor.Components.Dialog;
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
    public class CalendarField(CalendarFieldDesign design)
        : FieldBase<CalendarFieldDesign>(design), ISearchResultsViewField
    {
        private readonly ModuleCollection _modules = new();
        private SearchCondition? _additionalCondition;
        private (DateTime Start, DateTime End)? _currentRange;

        [ScriptHide]
        public Func<Task> OnDataChangedAsync { get; set; } = () => Task.CompletedTask;

        public bool AllowLoad { get; set; } = true;

        [ScriptHide]
        public string ModuleName => Design?.SearchCondition.ModuleName ?? string.Empty;

        public override bool IsModified => _modules.IsModified;

        internal List<ModuleCalendarItem> Items { get; } = [];

        public DateTime SelectedDate { get; private set; } = DateTime.Now;

        public CalendarViewMode ViewMode { get; private set; } = CalendarViewMode.Month;

        public DateTime RangeStart => GetViewDateRange().Start;

        public DateTime RangeEnd => GetViewDateRange().End;

        public int Page => 0;

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (!await _modules.ValidateInput()) return false;
            return await base.ValidateInput();
        }

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            ViewMode = GetDefaultViewMode();
            if (this.IsInLayout()) await ReloadAsync();
        }

        [ScriptName("SetAdditionalCondition")]
        public async Task SetAdditionalConditionAsync(ModuleSearcher searcher)
            => await SetAdditionalConditionAsync(searcher.GetSearchCondition(), 0);

        [ScriptHide]
        public async Task SetAdditionalConditionAsync(SearchCondition condition, int page)
        {
            await Task.CompletedTask;
            if (condition.ModuleName != Design.SearchCondition.ModuleName)
                throw LowCodeException.Create("{0} Invalid Module", Design.SearchCondition.ModuleName, condition.ModuleName);
            _additionalCondition = condition;
        }

        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
            => _modules.GetSubmitData(this, Design.SearchCondition);

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

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
            Items.RemoveAll(e => e.Module?.IsDeleted == true);
            await InvokeOnDataChangedAsync();
        }

        [ScriptMethodToProperty("SelectedDate")]
        public async Task SetSelectedDateAsync(DateTime date)
        {
            if (!await ConfirmDiscardChangesAsync()) return;
            SelectedDate = date;
            _currentRange = null;
            NotifyStateChanged();
            await ReloadAsync();
        }

        [ScriptMethodToProperty("ViewMode")]
        public async Task SetViewModeScriptAsync(CalendarViewMode mode)
        {
            if (!IsViewModeEnabled(mode)) return;
            if (!await ConfirmDiscardChangesAsync()) return;
            ViewMode = mode;
            _currentRange = null;
            NotifyStateChanged();
            await ReloadAsync();
        }

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (!AllowLoad) return;

            var (rangeStart, rangeEnd) = GetViewDateRange();

            if (_currentRange is { } range && range.Start == rangeStart && range.End == rangeEnd) return;

            _currentRange = (rangeStart, rangeEnd);

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

            var modules = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.DetailLayoutName);
            _modules.ApplyLoaded(modules);
            Items.Clear();
            Items.AddRange(modules.Select(ConvertToCalendarItem).OrderByStart());

            NotifyStateChanged();
        }

        internal bool IsViewModeEnabled(CalendarViewMode mode) => mode switch
        {
            CalendarViewMode.Month => Design.EnableMonthView,
            CalendarViewMode.Week => Design.EnableWeekView,
            CalendarViewMode.Day => Design.EnableDayView,
            _ => false,
        };

        internal (DateTime Start, DateTime End) GetViewDateRange()
        {
            return ViewMode switch
            {
                CalendarViewMode.Week => GetWeekRange(),
                CalendarViewMode.Day => (SelectedDate.Date, SelectedDate.Date.AddDays(1)),
                _ => GetMonthRange(),
            };
        }

        internal async Task AddAsync(DateTime date)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);

            await this.AssignConditionValuesAsync(Design.SearchCondition, mod);

            var targetDateTime = new DateTime(DateOnly.FromDateTime(date), TimeOnly.FromDateTime(DateTime.Now));

            SetDateFieldValue(mod, Design.StartField, targetDateTime);
            SetDateFieldValue(mod, Design.EndField, targetDateTime.AddHours(1));

            if (await mod.ShowDialogAsync(Properties.Resources.OK, Properties.Resources.Cancel) != Properties.Resources.OK) return;
            if (!await mod.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InputError);
                return;
            }

            _modules.Add(mod);
            Items.Add(ConvertToCalendarItem(mod));
            SortItems();

            await InvokeOnDataChangedAsync();
        }

        internal async Task EditAsync(Module? mod, bool viewOnly = false)
        {
            if (mod == null) return;

            if (viewOnly)
            {
                mod.IsViewOnly = true;
                await mod.ShowDialogAsync(Properties.Resources.Cancel);
                return;
            }

            var dialogResult = await mod.ShowDialogAsync(Properties.Resources.Update, Properties.Resources.Delete, Properties.Resources.Cancel);
            if (dialogResult == Properties.Resources.Update)
            {
                if (!await mod.ValidateInput())
                {
                    await Services.UIService.NotifyError(Properties.Resources.InputError);
                    return;
                }

                UpdateItemFromModule(mod);
                SortItems();
            }
            else if (dialogResult == Properties.Resources.Delete)
            {
                Items.RemoveAll(e => e.Module == mod);
                _modules.Remove(mod);
            }
            else
            {
                return;
            }

            await InvokeOnDataChangedAsync();
        }

        internal async Task SetCurrentDayAsync(DateTime date)
        {
            if (!await ConfirmDiscardChangesAsync()) return;
            SelectedDate = date;
            _currentRange = null;
            NotifyStateChanged();
            await ReloadAsync();
        }

        internal async Task SetViewModeAsync(CalendarViewMode mode)
        {
            if (!IsViewModeEnabled(mode)) return;
            if (!await ConfirmDiscardChangesAsync()) return;
            ViewMode = mode;
            _currentRange = null;
            NotifyStateChanged();
            await ReloadAsync();
        }

        private CalendarViewMode GetDefaultViewMode()
        {
            if (Design.EnableMonthView) return CalendarViewMode.Month;
            if (Design.EnableWeekView) return CalendarViewMode.Week;
            if (Design.EnableDayView) return CalendarViewMode.Day;
            return CalendarViewMode.Month;
        }

        private async Task InvokeOnDataChangedAsync()
        {
            await NotifyDataChangedAsync();
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
        }

        private void SortItems()
        {
            var sorted = Items.OrderByStart().ToList();
            Items.Clear();
            Items.AddRange(sorted);
        }

        private void UpdateItemFromModule(Module mod)
        {
            var item = Items.FirstOrDefault(e => e.Module?.GetIdText() == mod.GetIdText());
            if (item == null) return;

            var updated = ConvertToCalendarItem(mod);
            item.Text = updated.Text;
            item.Start = updated.Start;
            item.End = updated.End;
            item.AllDay = updated.AllDay;
            item.IsDateOnly = updated.IsDateOnly;
            item.Color = updated.Color;
        }

        private ModuleCalendarItem ConvertToCalendarItem(Module data)
        {
            var text = data.GetField<TextField>(Design.TextField)?.Value;
            var isDateOnly = IsDateField(data, Design.StartField);
            var start = GetDateTimeValue(data, Design.StartField);
            var end = GetDateTimeValue(data, Design.EndField);
            var allDay = data.GetField<BooleanField>(Design.AllDayField)?.Value ?? false;
            var color = string.IsNullOrEmpty(Design.ColorField) ? "" : data.GetField<TextField>(Design.ColorField)?.Value ?? "";

            if (text is null || start is null) return new() { Module = data };

            return new()
            {
                Module = data,
                Text = text,
                IsDateOnly = isDateOnly,
                Start = start.Value,
                End = end ?? start.Value,
                AllDay = allDay || end is null,
                Color = color,
            };
        }

        private static bool IsDateField(Module data, string fieldName)
            => !string.IsNullOrEmpty(fieldName) && data.GetField<DateField>(fieldName) != null;

        private static DateTime? GetDateTimeValue(Module data, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return null;

            var dateTimeField = data.GetField<DateTimeField>(fieldName);
            if (dateTimeField != null) return dateTimeField.Value;

            var dateField = data.GetField<DateField>(fieldName);
            if (dateField?.Value is { } dateOnly) return dateOnly.ToDateTime(TimeOnly.MinValue);

            return null;
        }

        private static void SetDateFieldValue(Module mod, string fieldName, DateTime value)
        {
            if (string.IsNullOrEmpty(fieldName)) return;

            var dateTimeField = mod.GetField<DateTimeField>(fieldName);
            if (dateTimeField != null)
            {
                dateTimeField.SetValueAsync(value).GetAwaiter().GetResult();
                return;
            }

            var dateField = mod.GetField<DateField>(fieldName);
            dateField?.SetValueAsync(DateOnly.FromDateTime(value)).GetAwaiter().GetResult();
        }

        private (DateTime Start, DateTime End) GetWeekRange()
        {
            var weekStart = SelectedDate.Date.AddDays(-(int)SelectedDate.DayOfWeek);
            return (weekStart, weekStart.AddDays(7));
        }

        private (DateTime Start, DateTime End) GetMonthRange()
        {
            var monthFirst = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
            return (monthFirst.AddDays(-7), monthFirst.AddMonths(1).AddDays(7));
        }

        private async Task<bool> ConfirmDiscardChangesAsync()
        {
            if (!_modules.IsModified) return true;
            var result = await Services.UIService.ShowMessageBox(
                string.Empty,
                Properties.Resources.DiscardChangesConfirmation,
                [new DialogButton("btn btn-outline-primary", Properties.Resources.Yes), new DialogButton("btn btn-outline-primary", Properties.Resources.No)]);
            return result == Properties.Resources.Yes;
        }

        private SearchCondition GetSearchCondition()
            => Design.SearchCondition.MergeSearchCondition(_additionalCondition);
    }

    internal static class CalendarItemExtensions
    {
        public static IOrderedEnumerable<ModuleCalendarItem> OrderByStart(this IEnumerable<ModuleCalendarItem> items)
            => items.OrderBy(e => e.Start).ThenByDescending(e => (e.End ?? e.Start) - e.Start);
    }
}
