using Codeer.LowCode.Blazor.Components.AppParts.Loading;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    internal class GanttItem
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Progress { get; set; }
        public string[] Dependencies { get; set; } = [];
        public Module? Module { get; set; }
    }

    internal record DependencyListItem(string FromLabel, string ToLabel, string From, string To, string Key);

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

        internal Dictionary<string, string[]> DependenciesMap { get; set; } = new();

        internal List<DependencyListItem> DependencyList { get; set; } = [];

        internal bool IsDateOnly { get; private set; }

        public DateTime ViewStart { get; private set; } = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        public GanttViewMode ViewMode { get; private set; } = GanttViewMode.Week;

        internal DateTime CustomRangeStart { get; set; } = DateTime.Today.AddMonths(-1);

        internal DateTime CustomRangeEnd { get; set; } = DateTime.Today.AddMonths(1);

        internal bool IsCustomRange => ViewMode == GanttViewMode.CustomRange;

        public DateTime RangeStart => GetViewDateRange().Start;

        public DateTime RangeEnd => GetViewDateRange().End;

        public int Page => 0;

        private bool HasDependenciesModule => !string.IsNullOrEmpty(Design?.DependenciesModule?.ModuleName);

        // ===== FieldBase overrides =====

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            IsDateOnly = DetectIsDateOnly();
            if (Design.CustomRange)
            {
                ViewMode = GanttViewMode.CustomRange;
                ViewStart = CustomRangeStart;
            }
            else
            {
                ViewMode = GetDefaultViewMode();
            }
            if (this.IsInLayout()) await ReloadAsync();
        }

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

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
            var removedCount = Items.RemoveAll(e => e.Module?.IsDeleted == true);
            if (removedCount == 0) return;

            MakeDependencyList();
            await InvokeOnDataChangedAndNotifyAsync();
        }

        // ===== Search condition =====

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

        // ===== Data loading =====

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (!AllowLoad) return;

            var (rangeStart, rangeEnd) = GetViewDateRange();

            if (HasDependenciesModule)
            {
                var depsItems = await this.GetChildModulesAsync(Design.DependenciesModule, ModuleLayoutType.None);
                DependenciesMap = depsItems.Select(ConvertToGanttDeps).GroupBy(e => e.Key)
                    .ToDictionary(e => e.Key, e => e.Select(f => f.Value).ToArray());
            }

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
            Items.AddRange(items.Select(ConvertToGanttItem).Where(e => e.Start != default).OrderBy(e => e.Start));

            MakeDependencyList();
            NotifyStateChanged();
        }

        // ===== View state =====

        [ScriptMethodToProperty("ViewStart")]
        public async Task SetViewStartAsync(DateTime date)
        {
            ViewStart = date;
            NotifyStateChanged();
            await ReloadAsync();
        }

        [ScriptMethodToProperty("ViewMode")]
        public async Task SetViewModeAsync(GanttViewMode mode)
        {
            if (!IsViewModeEnabled(mode)) return;
            ViewMode = mode;
            NotifyStateChanged();
            await ReloadAsync();
        }

        internal bool IsViewModeEnabled(GanttViewMode mode) => mode switch
        {
            GanttViewMode.Day => !IsDateOnly && Design.EnableDayView && !IsCustomRange,
            GanttViewMode.Week => Design.EnableWeekView && !IsCustomRange,
            GanttViewMode.Month => Design.EnableMonthView && !IsCustomRange,
            GanttViewMode.CustomRange => IsCustomRange,
            _ => false,
        };

        internal (DateTime Start, DateTime End) GetViewDateRange()
        {
            if (ViewMode == GanttViewMode.CustomRange)
                return (CustomRangeStart.Date, CustomRangeEnd.Date);

            var start = ViewStart.Date;
            return (ViewMode, Design.FitToWidth) switch
            {
                (GanttViewMode.Day, _) => (start, start.AddDays(1)),
                (GanttViewMode.Month, true) => (start, start.AddMonths(1)),
                (GanttViewMode.Month, false) => (start, start.AddDays(42)),
                (_, true) => (start, start.AddDays(7)),
                _ => (start, start.AddDays(14)),
            };
        }

        [ScriptMethod(ArgumentTypes = ["DateTime", "DateTime"], ArgumentNames = ["start", "end"])]
        public async Task SetCustomRangeAsync(DateTime start, DateTime end)
        {
            CustomRangeStart = start;
            CustomRangeEnd = end;
            ViewMode = GanttViewMode.CustomRange;
            ViewStart = start;
            NotifyStateChanged();
            await ReloadAsync();
        }


        // ===== CRUD operations =====

        internal async Task AddAsync(DateTime date)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);
            await mod.AssignRequiredCondition(Design.SearchCondition);

            await SetDateFieldValueAsync(mod, Design.StartField, date);
            await SetDateFieldValueAsync(mod, Design.EndField, date.AddDays(1));

            if (await mod.ShowDialogAsync(Properties.Resources.OK, Properties.Resources.Cancel) != Properties.Resources.OK) return;
            if (!mod.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InputError);
                return;
            }
            if (await mod.SubmitAsync() != true) return;

            Items.Add(ConvertToGanttItem(mod));
            SortItems();

            await InvokeOnDataChangedAndNotifyAsync();
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
                if (!mod.ValidateInput())
                {
                    await Services.UIService.NotifyError(Properties.Resources.InputError);
                    return;
                }
                if (await mod.SubmitAsync() != true) return;

                UpdateItemFromModule(mod);
                SortItems();
            }
            else if (dialogResult == Properties.Resources.Delete)
            {
                await mod.DeleteAsync();
                return;
            }
            else
            {
                return;
            }

            await InvokeOnDataChangedAndNotifyAsync();
        }

        internal async Task UpdateTaskDatesAsync(GanttItem item, DateTime newStart, DateTime newEnd)
        {
            if (item.Module == null) return;

            var saveStart = newStart;
            var saveEnd = newEnd;
            if (IsDateOnly)
            {
                saveStart = newStart.Date;
                saveEnd = newEnd.Date.AddDays(-1);
                if (saveEnd < saveStart) saveEnd = saveStart;
            }

            await SetDateFieldValueAsync(item.Module, Design.StartField, saveStart);
            await SetDateFieldValueAsync(item.Module, Design.EndField, saveEnd);

            using var scope = Services.Provider.GetService<LoadingService>()?.StartLoading(300);
            if (await item.Module.SubmitAsync() != true) return;

            item.Start = newStart;
            item.End = newEnd;

            await InvokeOnDataChangedAndNotifyAsync();
        }

        // ===== Dependency operations =====

        internal async Task AddDependenciesAsync(string sourceId, string destinationId)
        {
            if (!HasDependenciesModule) return;

            var srcTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == sourceId);
            var dstTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == destinationId);
            if (srcTask?.Module == null || dstTask?.Module == null) return;

            await IncrementProcessingCounters(srcTask.Module, dstTask.Module);

            var mod = await this.CreateChildModuleAsync(Design.DependenciesModule.ModuleName, ModuleLayoutType.None);
            if (!await SetDepFieldValueAsync(mod, Design.DependencySourceIdField, sourceId)) return;
            if (!await SetDepFieldValueAsync(mod, Design.DependencyDestinationIdField, destinationId)) return;

            if (await mod.SubmitAsync([srcTask.Module, dstTask.Module]) != true) return;

            AddToDependenciesMap(destinationId, sourceId);
            UpdateItemDependencies();
            MakeDependencyList();

            await ReloadDependencyTasksAsync(sourceId, destinationId, srcTask, dstTask);

            await InvokeOnDataChangedAndNotifyAsync();
        }

        internal async Task DeleteDependenciesAsync(string sourceId, string destinationId)
        {
            if (!HasDependenciesModule) return;

            var srcTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == sourceId);
            var dstTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == destinationId);
            if (srcTask?.Module == null || dstTask?.Module == null) return;

            var srcSubmitData = srcTask.Module.GetSubmitData();
            srcSubmitData.SearchDelete.Add(new SearchCondition
            {
                ModuleName = Design.DependenciesModule.ModuleName,
                Condition = MultiMatchCondition.And(
                    new VariableName($"{Design.DependencySourceIdField}.Value").Equal(sourceId),
                    new VariableName($"{Design.DependencyDestinationIdField}.Value").Equal(destinationId))
            });

            RemoveFromDependenciesMap(destinationId, sourceId);
            UpdateItemDependencies();
            MakeDependencyList();

            await Services.ModuleDataService.SubmitAsync(
                [srcSubmitData, dstTask.Module.GetSubmitData()]);

            await InvokeOnDataChangedAndNotifyAsync();
        }

        // ===== Private helpers =====

        private async Task InvokeOnDataChangedAndNotifyAsync()
        {
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        private void SortItems()
            => Items.Sort((a, b) => a.Start.CompareTo(b.Start));

        private void UpdateItemFromModule(Module mod)
        {
            var item = Items.FirstOrDefault(e => e.Module?.GetIdText() == mod.GetIdText());
            if (item == null) return;
            item.Text = mod.GetField<TextField>(Design.TextField)?.Value ?? item.Text;
            item.Start = GetDateTimeValue(mod, Design.StartField) ?? item.Start;
            item.End = GetDateTimeValue(mod, Design.EndField) ?? item.End;
            item.Progress = GetProgressValue(mod);
        }

        private GanttItem ConvertToGanttItem(Module data)
        {
            var id = data.GetIdText() ?? string.Empty;
            var start = GetDateTimeValue(data, Design.StartField);
            var end = GetDateTimeValue(data, Design.EndField);

            var s = start ?? default;
            var e = end ?? start ?? default;

            if (IsDateOnly)
            {
                s = s.Date;
                e = e.Date;
                if (e <= s) e = s.AddDays(1);
                else e = e.AddDays(1);
            }

            return new GanttItem
            {
                Module = data,
                Id = id,
                Text = data.GetField<TextField>(Design.TextField)?.Value ?? string.Empty,
                Start = s,
                End = e,
                Progress = GetProgressValue(data),
                Dependencies = DependenciesMap.TryGetValue(id, out var deps) ? deps : [],
            };
        }

        private int GetProgressValue(Module data)
        {
            if (string.IsNullOrEmpty(Design.ProgressField)) return 0;
            var val = data.GetField<NumberField>(Design.ProgressField)?.Value;
            return int.TryParse(val?.ToString() ?? string.Empty, out var p) ? Math.Clamp(p, 0, 100) : 0;
        }

        private bool DetectIsDateOnly()
        {
            if (string.IsNullOrEmpty(Design.StartField)) return false;
            var moduleName = Design.SearchCondition.ModuleName;
            if (string.IsNullOrEmpty(moduleName)) return false;
            var moduleDesign = Services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            if (moduleDesign == null) return false;
            var fieldDesign = moduleDesign.Fields.FirstOrDefault(f => f.Name == Design.StartField);
            return fieldDesign is DateFieldDesign;
        }

        private GanttViewMode GetDefaultViewMode()
        {
            if (Design.EnableWeekView) return GanttViewMode.Week;
            if (Design.EnableMonthView) return GanttViewMode.Month;
            if (Design.EnableDayView) return GanttViewMode.Day;
            return GanttViewMode.Week;
        }

        private static DateTime? GetDateTimeValue(Module data, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return null;
            var dateTimeField = data.GetField<DateTimeField>(fieldName);
            if (dateTimeField != null) return dateTimeField.Value;
            var dateField = data.GetField<DateField>(fieldName);
            if (dateField?.Value is { } dateOnly) return dateOnly.ToDateTime(TimeOnly.MinValue);
            return null;
        }

        private static async Task SetDateFieldValueAsync(Module mod, string fieldName, DateTime value)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            var dateTimeField = mod.GetField<DateTimeField>(fieldName);
            if (dateTimeField != null)
            {
                await dateTimeField.SetValueAsync(value);
                return;
            }
            var dateField = mod.GetField<DateField>(fieldName);
            if (dateField != null) await dateField.SetValueAsync(DateOnly.FromDateTime(value));
        }

        // ===== Dependency helpers =====

        private KeyValuePair<string, string> ConvertToGanttDeps(Module data)
        {
            var source = GetDepFieldValue(data, Design.DependencySourceIdField);
            var destination = GetDepFieldValue(data, Design.DependencyDestinationIdField);
            return new KeyValuePair<string, string>(destination ?? "", source ?? "");
        }

        private void UpdateItemDependencies()
        {
            foreach (var item in Items)
                item.Dependencies = DependenciesMap.TryGetValue(item.Id, out var deps) ? deps : [];
        }

        private void MakeDependencyList()
        {
            DependencyList = DependenciesMap
                .SelectMany(pair => pair.Value.Select(from => new DependencyListItem(
                    Items.FirstOrDefault(e => e.Id == from)?.Text ?? "",
                    Items.FirstOrDefault(e => e.Id == pair.Key)?.Text ?? "",
                    from, pair.Key, $"{from}->{pair.Key}")))
                .OrderBy(e => e.Key)
                .ToList();
        }

        private void AddToDependenciesMap(string destinationId, string sourceId)
        {
            if (DependenciesMap.TryGetValue(destinationId, out var existing))
                DependenciesMap[destinationId] = [.. existing, sourceId];
            else
                DependenciesMap[destinationId] = [sourceId];
        }

        private void RemoveFromDependenciesMap(string destinationId, string sourceId)
        {
            if (!DependenciesMap.TryGetValue(destinationId, out var existing)) return;
            var updated = existing.Where(e => e != sourceId).ToArray();
            if (updated.Length == 0)
                DependenciesMap.Remove(destinationId);
            else
                DependenciesMap[destinationId] = updated;
        }

        private async Task IncrementProcessingCounters(Module srcModule, Module dstModule)
        {
            if (string.IsNullOrEmpty(Design.ProcessingCounterField)) return;

            var srcCounter = srcModule.GetField<NumberField>(Design.ProcessingCounterField);
            var dstCounter = dstModule.GetField<NumberField>(Design.ProcessingCounterField);
            if (srcCounter != null) await srcCounter.SetValueAsync(srcCounter.Value + 1);
            if (dstCounter != null) await dstCounter.SetValueAsync(dstCounter.Value + 1);
        }

        private async Task ReloadDependencyTasksAsync(string sourceId, string destinationId, GanttItem srcTask, GanttItem dstTask)
        {
            if (string.IsNullOrEmpty(Design.IdField)) return;

            var idVariable = new VariableName($"{Design.IdField}.Value");
            var reloadCondition = new SearchCondition
            {
                ModuleName = Design.SearchCondition.ModuleName,
                Condition = MultiMatchCondition.Or(idVariable.Equal(sourceId), idVariable.Equal(destinationId))
            };
            var mods = await this.GetChildModulesAsync(reloadCondition, ModuleLayoutType.Detail, Design.DetailLayoutName);
            srcTask.Module = mods.FirstOrDefault(e => e.GetIdText() == sourceId);
            dstTask.Module = mods.FirstOrDefault(e => e.GetIdText() == destinationId);
        }

        private static string? GetDepFieldValue(Module data, string fieldName)
        {
            var idField = data.GetField<IdField>(fieldName);
            if (idField != null) return idField.Value;
            return data.GetField<LinkField>(fieldName)?.Value;
        }

        private static async Task<bool> SetDepFieldValueAsync(Module data, string fieldName, string value)
        {
            var idField = data.GetField<IdField>(fieldName);
            if (idField != null)
            {
                await idField.SetValueAsync(value);
                return true;
            }
            var linkField = data.GetField<LinkField>(fieldName);
            if (linkField != null)
            {
                await linkField.SetValueAsync(value);
                return true;
            }
            return false;
        }

        private SearchCondition GetSearchCondition()
            => Design.SearchCondition.MergeSearchCondition(_additionalCondition);
    }
}
