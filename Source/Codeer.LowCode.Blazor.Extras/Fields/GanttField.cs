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
    public enum GanttViewMode
    {
        Day,
        Week,
        Month
    }

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

        public DateTime ViewStart { get; private set; } = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        public GanttViewMode ViewMode { get; private set; } = GanttViewMode.Week;

        public DateTime RangeStart => GetViewDateRange().Start;

        public DateTime RangeEnd => GetViewDateRange().End;

        public int Page => 0;

        private bool HasDependenciesModule => !string.IsNullOrEmpty(Design?.DependenciesModule?.ModuleName);

        // ===== FieldBase overrides =====

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
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
            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
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
        public async Task SetViewStartScriptAsync(DateTime date)
        {
            ViewStart = date;
            NotifyStateChanged();
            await ReloadAsync();
        }

        [ScriptMethodToProperty("ViewMode")]
        public async Task SetViewModeScriptAsync(GanttViewMode mode)
        {
            ViewMode = mode;
            NotifyStateChanged();
            await ReloadAsync();
        }

        internal (DateTime Start, DateTime End) GetViewDateRange() => ViewMode switch
        {
            GanttViewMode.Day => (ViewStart.Date, ViewStart.Date.AddDays(1)),
            GanttViewMode.Month => (ViewStart.Date, ViewStart.Date.AddDays(42)),
            _ => (ViewStart.Date, ViewStart.Date.AddDays(14)),
        };

        internal Task SetViewStartAsync(DateTime date) => SetViewStartScriptAsync(date);

        internal Task SetViewModeAsync(GanttViewMode mode) => SetViewModeScriptAsync(mode);

        // ===== CRUD operations =====

        internal async Task AddAsync(DateTime date)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);
            await mod.AssignRequiredCondition(Design.SearchCondition);

            var start = mod.GetField<DateTimeField>(Design.StartField);
            var end = mod.GetField<DateTimeField>(Design.EndField);
            if (start != null) await start.SetValueAsync(date);
            if (end != null) await end.SetValueAsync(date.AddDays(1));

            if (await mod.ShowDialogAsync(Properties.Resources.OK, Properties.Resources.Cancel) != Properties.Resources.OK) return;
            if (!mod.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InputError);
                return;
            }
            if (await mod.SubmitAsync() != true) return;

            Items.Add(ConvertToGanttItem(mod));
            SortItems();

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
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

            using var scope = Services.Provider.GetService<LoadingService>()?.StartLoading(300);
            if (await item.Module.SubmitAsync() != true) return;

            item.Start = newStart;
            item.End = newEnd;

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
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

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
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

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        // ===== Private helpers =====

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

        private void UpdateItemFromModule(Module mod)
        {
            var item = Items.FirstOrDefault(e => e.Module?.GetIdText() == mod.GetIdText());
            if (item == null) return;
            item.Text = mod.GetField<TextField>(Design.TextField)?.Value ?? item.Text;
            item.Start = mod.GetField<DateTimeField>(Design.StartField)?.Value ?? item.Start;
            item.End = mod.GetField<DateTimeField>(Design.EndField)?.Value ?? item.End;
            item.Progress = GetProgressValue(mod);
        }

        private GanttItem ConvertToGanttItem(Module data)
        {
            var id = data.GetIdText() ?? string.Empty;
            return new GanttItem
            {
                Module = data,
                Id = id,
                Text = data.GetField<TextField>(Design.TextField)?.Value ?? string.Empty,
                Start = data.GetField<DateTimeField>(Design.StartField)?.Value ?? default,
                End = data.GetField<DateTimeField>(Design.EndField)?.Value
                      ?? data.GetField<DateTimeField>(Design.StartField)?.Value ?? default,
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
