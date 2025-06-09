﻿using Codeer.LowCode.Blazor.DataIO;
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
    public class ModuleCalendarItem
    {
        public DateTime Start { get; set; }

        public DateTime? End { get; set; }

        public bool AllDay { get; set; }

        public string Text { get; set; } = string.Empty;
        public Module? Module { get; set; }
    }

    public class CalendarField(CalendarFieldDesign design)
        : FieldBase<CalendarFieldDesign>(design), ISearchResultsViewField
    {

        public int Year = 2024;
        public int Month = 7;
        public List<DateTime?> Days => GenerateDays();
        
        
        List<DateTime?> GenerateDays()
        {
            var days = new List<DateTime?>();

            var firstDayOfMonth = new DateTime(Year, Month, 1);
            int daysInMonth = DateTime.DaysInMonth(Year, Month);
            int startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;

            for (int i = 0; i < startDayOfWeek; i++) days.Add(null);
            for (int i = 1; i <= daysInMonth; i++) days.Add(new DateTime(Year, Month, i));
            while (days.Count < 42) days.Add(null);

            return days;
        }





        private SearchCondition? _additionalCondition;
        private DateTime? _currentMonth;

        [ScriptHide]
        public Func<SearchCondition?, Task> OnQueryChangedAsync { get; set; } = _ => Task.CompletedTask;

        [ScriptHide]
        public Func<Task> OnSearchDataChangedAsync { get; set; } = async () => await Task.CompletedTask;

        [ScriptHide]
        public Func<Task> OnDataChangedAsync { get; set; } = async () => await Task.CompletedTask;

        public bool AllowLoad { get; set; } = true;

        [ScriptHide]
        public string ModuleName => Design?.SearchCondition.ModuleName ?? string.Empty;

        public override bool IsModified => false;

        internal List<ModuleCalendarItem> Items { get; } = [];

        internal DateTime SelectedDate { get; private set; } = DateTime.Now;

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
            if (condition.ModuleName != Design.SearchCondition.ModuleName) throw LowCodeException.Create("{0} Invalid Module", Design.SearchCondition.ModuleName, condition.ModuleName);
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

            var date = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);

            if (_currentMonth is not null && _currentMonth == date) return;

            _currentMonth = date;

            var startDate = _currentMonth.Value.AddDays(-7);
            var endDate = _currentMonth.Value.AddMonths(1).AddDays(7);
            var startVariable = new VariableName($"{Design.StartField}.Value");
            var endVariable = new VariableName($"{Design.EndField}.Value");
            var condition = new SearchCondition
            {
                ModuleName = Design.SearchCondition.ModuleName,
                Condition = MultiMatchCondition.Or(
                    MultiMatchCondition.And(startVariable.GreaterThanOrEqual(startDate), startVariable.LessThan(endDate)),
                    MultiMatchCondition.And(endVariable.GreaterThanOrEqual(startDate), endVariable.LessThan(endDate))
                    )
            };

            await SetAdditionalConditionAsync(condition, 0);

            var items = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.DetailLayoutName);
            Items.Clear();
            Items.AddRange(items.Select(ConvertToCalendarItem).ToList());

            NotifyStateChanged();
        }

        internal async Task AddAsync(DateTime date)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);

            await mod.AssignRequiredCondition(Design.SearchCondition);

            var targetDateTime = new DateTime(DateOnly.FromDateTime(date), TimeOnly.FromDateTime(DateTime.Now));

            var start = mod.GetField<DateTimeField>(Design.StartField);
            var end = mod.GetField<DateTimeField>(Design.EndField);
            if (start != null) await start.SetValueAsync(targetDateTime);
            if (end != null) await end.SetValueAsync(targetDateTime.AddHours(1));

            if (await mod.ShowDialogAsync("OK", "Cancel") != "OK") return;
            if (!mod.ValidateInput())
            {
                await Services.UIService.NotifyError("Input Error");
                return;
            }
            if (await mod.SubmitAsync() != true) return;

            Items.Add(ConvertToCalendarItem(mod)!);

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
                        item.AllDay = mod.GetField<BooleanField>(Design.AllDayField)?.Value ?? item.AllDay;
                    }
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

        internal async Task SetCurrentDayAsync(DateTime date)
        {
            SelectedDate = date;
            await ReloadAsync();
        }

        private async Task InvokeOnDataChangedAsync()
        {
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
        }

        private ModuleCalendarItem ConvertToCalendarItem(Module data)
        {
            var design = Design;
            var text = data.GetField<TextField>(design.TextField)?.Value;
            var start = data.GetField<DateTimeField>(design.StartField)?.Value;
            var end = data.GetField<DateTimeField>(design.EndField)?.Value;
            var allDay = data.GetField<BooleanField>(design.AllDayField)?.Value ?? false;

            if (text is null || start is null) return new() { Module = data };

            return new()
            {
                Module = data,
                Text = text,
                Start = start.Value,
                End = end ?? start.Value,
                AllDay = allDay || end is null,
            };
        }

        private SearchCondition GetSearchCondition()
            => Design.SearchCondition.MergeSearchCondition(_additionalCondition);
    }
}
