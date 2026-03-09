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

    public class GanttItem
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public int Progress { get; set; }
        public string[] Dependencies { get; set; } = [];
        public Module? Module { get; set; }
    }

    public record DependencyListItem(string FromLabel, string ToLabel, string From, string To, string Key);

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

        internal DateTime ViewStart { get; private set; } = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        internal GanttViewMode ViewMode { get; private set; } = GanttViewMode.Week;

        public int Page => 0;

        private bool HasDependenciesModule => !string.IsNullOrEmpty(Design?.DependenciesModule?.ModuleName);

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

            MakeDependencyList();
            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (!AllowLoad) return;

            var (rangeStart, rangeEnd) = GetViewDateRange();

            // 依存関係を先に取得
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
            Items.AddRange(items.Select(ConvertToGanttItem).Where(e => e.Start != default).OrderBy(e => e.Start).ToList());

            MakeDependencyList();
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

                var item = Items.FirstOrDefault(e => e.Module?.GetIdText() == mod.GetIdText());
                if (item != null)
                {
                    item.Text = mod.GetField<TextField>(Design.TextField)?.Value ?? item.Text;
                    item.Start = mod.GetField<DateTimeField>(Design.StartField)?.Value ?? item.Start;
                    item.End = mod.GetField<DateTimeField>(Design.EndField)?.Value ?? item.End;
                    item.Progress = GetProgressValue(mod);
                }
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

        internal async Task AddDependenciesAsync(string sourceId, string destinationId)
        {
            if (!HasDependenciesModule) return;

            var srcTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == sourceId);
            var dstTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == destinationId);
            if (srcTask?.Module == null || dstTask?.Module == null) return;

            // ProcessingCounterField をインクリメント
            if (!string.IsNullOrEmpty(Design.ProcessingCounterField))
            {
                var sourceCounterField = srcTask.Module.GetField<NumberField>(Design.ProcessingCounterField);
                var destinationCounterField = dstTask.Module.GetField<NumberField>(Design.ProcessingCounterField);
                if (sourceCounterField != null)
                    await sourceCounterField.SetValueAsync(sourceCounterField.Value + 1);
                if (destinationCounterField != null)
                    await destinationCounterField.SetValueAsync(destinationCounterField.Value + 1);
            }

            // 依存関係モジュールに新規レコード作成
            var mod = await this.CreateChildModuleAsync(Design.DependenciesModule.ModuleName, ModuleLayoutType.None);
            if (!await SetDepFieldValueAsync(mod, Design.DependencySourceIdField, sourceId)) return;
            if (!await SetDepFieldValueAsync(mod, Design.DependencyDestinationIdField, destinationId)) return;

            if (await mod.SubmitAsync([srcTask.Module, dstTask.Module]) != true) return;

            // メモリ上の依存関係を更新
            if (DependenciesMap.ContainsKey(destinationId))
            {
                var deps = DependenciesMap[destinationId].ToList();
                deps.Add(sourceId);
                DependenciesMap[destinationId] = deps.ToArray();
            }
            else
            {
                DependenciesMap[destinationId] = [sourceId];
            }

            UpdateItemDependencies();
            MakeDependencyList();

            // DB から最新データを再取得（楽観ロック Version 同期）
            if (!string.IsNullOrEmpty(Design.IdField))
            {
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

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        internal async Task DeleteDependenciesAsync(string sourceId, string destinationId)
        {
            if (!HasDependenciesModule) return;

            var srcTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == sourceId);
            var dstTask = Items.FirstOrDefault(e => e.Module?.GetIdText() == destinationId);
            if (srcTask?.Module == null || dstTask?.Module == null) return;

            // SubmitData に SearchDelete 条件を追加
            var srcSubmitData = srcTask.Module.GetSubmitData();
            srcSubmitData.SearchDelete.Add(new SearchCondition
            {
                ModuleName = Design.DependenciesModule.ModuleName,
                Condition = MultiMatchCondition.And(
                    new VariableName($"{Design.DependencySourceIdField}.Value").Equal(sourceId),
                    new VariableName($"{Design.DependencyDestinationIdField}.Value").Equal(destinationId))
            });

            // メモリ上の依存関係を更新
            if (DependenciesMap.ContainsKey(destinationId))
            {
                var deps = DependenciesMap[destinationId].ToList();
                deps.Remove(sourceId);
                if (deps.Count == 0)
                    DependenciesMap.Remove(destinationId);
                else
                    DependenciesMap[destinationId] = deps.ToArray();
            }

            UpdateItemDependencies();
            MakeDependencyList();

            // 送信
            await Services.ModuleDataService.SubmitAsync(
                new[] { srcSubmitData, dstTask.Module.GetSubmitData() }.ToList());

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
            var id = data.GetIdText() ?? string.Empty;

            return new GanttItem
            {
                Module = data,
                Id = id,
                Text = text ?? string.Empty,
                Start = start ?? default,
                End = end ?? start ?? default,
                Progress = GetProgressValue(data),
                Dependencies = DependenciesMap.TryGetValue(id, out var deps) ? deps : [],
            };
        }

        private KeyValuePair<string, string> ConvertToGanttDeps(Module data)
        {
            var source = GetDepFieldValue(data, Design.DependencySourceIdField);
            var destination = GetDepFieldValue(data, Design.DependencyDestinationIdField);
            return new KeyValuePair<string, string>(destination ?? "", source ?? "");
        }

        private void UpdateItemDependencies()
        {
            foreach (var item in Items)
            {
                item.Dependencies = DependenciesMap.TryGetValue(item.Id, out var deps) ? deps : [];
            }
        }

        private void MakeDependencyList()
        {
            DependencyList.Clear();
            foreach (var pair in DependenciesMap)
            {
                var toLabel = Items.FirstOrDefault(e => e.Id == pair.Key)?.Text ?? "";
                foreach (var from in pair.Value)
                {
                    var fromLabel = Items.FirstOrDefault(e => e.Id == from)?.Text ?? "";
                    DependencyList.Add(new DependencyListItem(fromLabel, toLabel, from, pair.Key, $"{from}->{pair.Key}"));
                }
            }
            DependencyList = DependencyList.OrderBy(item => item.Key).ToList();
        }

        private int GetProgressValue(Module data)
        {
            if (string.IsNullOrEmpty(Design.ProgressField)) return 0;
            var val = data.GetField<NumberField>(Design.ProgressField)?.Value;
            return int.TryParse(val?.ToString() ?? string.Empty, out var p) ? Math.Clamp(p, 0, 100) : 0;
        }

        private static string? GetDepFieldValue(Module data, string fieldName)
        {
            var idField = data.GetField<IdField>(fieldName);
            if (idField != null) return idField.Value;
            var linkField = data.GetField<LinkField>(fieldName);
            return linkField?.Value;
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
