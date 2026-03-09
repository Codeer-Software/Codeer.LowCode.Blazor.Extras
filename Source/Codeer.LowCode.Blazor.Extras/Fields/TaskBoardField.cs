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
    public class TaskBoardItem
    {
        public string StatusValue { get; set; } = string.Empty;
        public int SortIndex { get; set; }
        public Module? Module { get; set; }
    }

    public class TaskBoardField(TaskBoardFieldDesign design)
        : FieldBase<TaskBoardFieldDesign>(design), ISearchResultsViewField
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

        internal List<TaskBoardItem> Items { get; } = [];

        internal List<TaskBoardStatusDesign> StatusCategories { get; private set; } = [];

        public int Page => 0;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            ParseStatusCategories();
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

            var items = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.DetailLayoutName);
            Items.Clear();
            Items.AddRange(items.Select(ConvertToTaskBoardItem).OrderBy(e => e.SortIndex).ToList());

            NotifyStateChanged();
        }

        internal async Task AddAsync(string statusValue)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);
            await mod.AssignRequiredCondition(Design.SearchCondition);

            await SetStatusValueAsync(mod, statusValue);

            if (HasSortIndex)
            {
                var maxIndex = Items.Where(e => e.StatusValue == statusValue).Select(e => e.SortIndex).DefaultIfEmpty(-1).Max();
                await SetSortIndexValueAsync(mod, maxIndex + 1);
            }

            if (await mod.ShowDialogAsync(Properties.Resources.OK, Properties.Resources.Cancel) != Properties.Resources.OK) return;
            if (!mod.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InputError);
                return;
            }
            if (await mod.SubmitAsync() != true) return;

            Items.Add(ConvertToTaskBoardItem(mod));

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        internal async Task EditAsync(Module? mod)
        {
            if (mod == null) return;
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
                    item.StatusValue = GetStatusValue(mod);
                    item.SortIndex = GetSortIndexValue(mod);
                }
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

        internal async Task MoveTaskAsync(TaskBoardItem item, string newStatusValue, int newIndex)
        {
            if (item.Module == null) return;

            var statusChanged = item.StatusValue != newStatusValue;

            if (statusChanged)
            {
                await SetStatusValueAsync(item.Module, newStatusValue);
                item.StatusValue = newStatusValue;
            }

            if (HasSortIndex)
            {
                var columnItems = Items
                    .Where(e => e.StatusValue == newStatusValue && e != item)
                    .OrderBy(e => e.SortIndex)
                    .ToList();

                if (newIndex < 0) newIndex = 0;
                if (newIndex > columnItems.Count) newIndex = columnItems.Count;
                columnItems.Insert(newIndex, item);

                for (int i = 0; i < columnItems.Count; i++)
                {
                    columnItems[i].SortIndex = i;
                    if (columnItems[i].Module != null)
                    {
                        await SetSortIndexValueAsync(columnItems[i].Module!, i);
                    }
                }
            }

            using var scope = Services.Provider.GetService<LoadingService>()?.StartLoading(300);

            var modifiedModules = Items
                .Where(e => e.Module?.IsModified == true)
                .Select(e => e.Module!)
                .ToList();

            foreach (var mod in modifiedModules)
            {
                await mod.SubmitAsync();
            }

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        internal List<TaskBoardItem> GetColumnItems(string statusValue)
        {
            return Items
                .Where(e => e.StatusValue == statusValue)
                .OrderBy(e => e.SortIndex)
                .ToList();
        }

        private void ParseStatusCategories()
        {
            StatusCategories = Design.Statuses.Items.Select(e => new TaskBoardStatusDesign
            {
                DisplayText = e.DisplayText,
                Value = string.IsNullOrEmpty(e.Value) ? e.DisplayText : e.Value,
                Color = e.Color,
                CanAdd = e.CanAdd,
            }).ToList();
        }

        private async Task InvokeOnDataChangedAsync()
        {
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
        }

        private TaskBoardItem ConvertToTaskBoardItem(Module data)
        {
            return new TaskBoardItem
            {
                Module = data,
                StatusValue = GetStatusValue(data),
                SortIndex = GetSortIndexValue(data),
            };
        }

        private string GetStatusValue(Module data)
        {
            var selectField = data.GetField<SelectField>(Design.StatusField);
            if (selectField != null) return selectField.Value ?? string.Empty;

            var textField = data.GetField<TextField>(Design.StatusField);
            if (textField != null) return textField.Value ?? string.Empty;

            return string.Empty;
        }

        private async Task SetStatusValueAsync(Module data, string value)
        {
            var selectField = data.GetField<SelectField>(Design.StatusField);
            if (selectField != null)
            {
                await selectField.SetValueAsync(value);
                return;
            }

            var textField = data.GetField<TextField>(Design.StatusField);
            if (textField != null)
            {
                await textField.SetValueAsync(value);
            }
        }

        private bool HasSortIndex => !string.IsNullOrEmpty(Design.SortIndexField);

        private int GetSortIndexValue(Module data)
        {
            if (!HasSortIndex) return 0;
            var field = data.GetField<NumberField>(Design.SortIndexField);
            return (int)(field?.Value ?? 0);
        }

        private async Task SetSortIndexValueAsync(Module data, int value)
        {
            if (!HasSortIndex) return;
            var field = data.GetField<NumberField>(Design.SortIndexField);
            if (field != null) await field.SetValueAsync(value);
        }

        private SearchCondition GetSearchCondition()
            => Design.SearchCondition.MergeSearchCondition(_additionalCondition);
    }
}
