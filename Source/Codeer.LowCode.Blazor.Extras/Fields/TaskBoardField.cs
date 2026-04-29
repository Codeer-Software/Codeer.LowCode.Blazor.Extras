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
    public class TaskBoardField(TaskBoardFieldDesign design)
        : FieldBase<TaskBoardFieldDesign>(design), ISearchResultsViewField
    {
        private readonly ModuleCollection _modules = new();
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

        public override bool IsModified => _modules.IsModified;

        internal List<TaskBoardItem> Items { get; } = [];

        internal List<TaskBoardStatus> StatusCategories => Design.Statuses.Items;

        public int Page => 0;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            if (this.IsInLayout()) await ReloadAsync();
        }

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (!await _modules.ValidateInput()) return false;
            return await base.ValidateInput();
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

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (!AllowLoad) return;

            var items = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.CardLayoutName, GetLayoutFieldNames(Design.PopupLayoutName));
            _modules.ApplyLoaded(items);
            Items.Clear();
            Items.AddRange(items.Select(ConvertToTaskBoardItem).OrderBy(e => e.SortIndex));

            NotifyStateChanged();
        }

        private IEnumerable<string>? GetLayoutFieldNames(string layoutName)
        {
            var moduleDesign = Services.AppInfoService.GetDesignData().Modules.Find(Design.SearchCondition.ModuleName);
            if (moduleDesign == null) return null;
            if (!moduleDesign.DetailLayouts.TryGetValue(layoutName, out var layout)) return null;
            return layout.Layout.GetDescendantFields(moduleDesign).Select(e => e.Name);
        }

        internal async Task AddAsync(string statusValue)
        {
            var popupMod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.PopupLayoutName);
            await this.AssignConditionValuesAsync(Design.SearchCondition, popupMod);

            await SetFieldStringValueAsync(popupMod, Design.StatusField, statusValue);

            if (HasSortIndex)
            {
                var maxIndex = Items.Where(e => e.StatusValue == statusValue).Select(e => e.SortIndex).DefaultIfEmpty(-1).Max();
                await SetSortIndexValueAsync(popupMod, maxIndex + 1);
            }

            if (await popupMod.ShowDialogAsync(Properties.Resources.OK, Properties.Resources.Cancel) != Properties.Resources.OK) return;
            if (!await popupMod.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InputError);
                return;
            }

            Module cardMod;
            if (IsSameLayout)
            {
                cardMod = popupMod;
            }
            else
            {
                cardMod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.CardLayoutName);
                await ModuleHelper.CopyFieldDataAsync(popupMod, cardMod);
            }

            _modules.Add(cardMod);
            Items.Add(ConvertToTaskBoardItem(cardMod));

            await InvokeOnDataChangedAsync();
        }

        internal async Task EditAsync(Module? cardMod, bool viewOnly = false)
        {
            if (cardMod == null) return;

            var popupMod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.PopupLayoutName);
            await ModuleHelper.CopyFieldDataAsync(cardMod, popupMod);

            if (viewOnly)
            {
                popupMod.IsViewOnly = true;
                await popupMod.ShowDialogAsync(Properties.Resources.Cancel);
                return;
            }

            var dialogResult = await popupMod.ShowDialogAsync(Properties.Resources.Update, Properties.Resources.Delete, Properties.Resources.Cancel);
            if (dialogResult == Properties.Resources.Update)
            {
                if (!await popupMod.ValidateInput())
                {
                    await Services.UIService.NotifyError(Properties.Resources.InputError);
                    return;
                }

                await ModuleHelper.CopyFieldDataAsync(popupMod, cardMod);

                var item = Items.FirstOrDefault(e => e.Module?.GetIdText() == cardMod.GetIdText());
                if (item != null)
                {
                    item.StatusValue = GetFieldStringValue(cardMod, Design.StatusField);
                    item.SortIndex = GetSortIndexValue(cardMod);
                }
            }
            else if (dialogResult == Properties.Resources.Delete)
            {
                Items.RemoveAll(e => e.Module == cardMod);
                _modules.Remove(cardMod);
            }
            else
            {
                return;
            }

            await InvokeOnDataChangedAsync();
        }

        private bool IsSameLayout => Design.CardLayoutName == Design.PopupLayoutName;

        bool _moving = false;
        internal async Task MoveTaskAsync(TaskBoardItem item, string newStatusValue, int newIndex)
        {
            _moving = true;
            try
            {
                if (item.Module == null) return;

                if (item.StatusValue != newStatusValue)
                {
                    await SetFieldStringValueAsync(item.Module, Design.StatusField, newStatusValue);
                    item.StatusValue = newStatusValue;
                }

                if (HasSortIndex)
                {
                    var columnItems = Items
                        .Where(e => e.StatusValue == newStatusValue && e != item)
                        .OrderBy(e => e.SortIndex)
                        .ToList();

                    newIndex = Math.Clamp(newIndex, 0, columnItems.Count);
                    columnItems.Insert(newIndex, item);

                    for (var i = 0; i < columnItems.Count; i++)
                    {
                        columnItems[i].SortIndex = i;
                        if (columnItems[i].Module != null)
                            await SetSortIndexValueAsync(columnItems[i].Module!, i);
                    }
                }
            }
            finally
            {
                _moving = false;
            }
            await InvokeOnDataChangedAsync();
        }

        internal List<TaskBoardItem> GetColumnItems(string statusValue)
            => Items.Where(e => e.StatusValue == statusValue).OrderBy(e => e.SortIndex).ToList();

        private async Task InvokeOnDataChangedAsync()
        {
            if (_moving) return;
            await NotifyDataChangedAsync();
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
        }

        private TaskBoardItem ConvertToTaskBoardItem(Module data) => new()
        {
            Module = data,
            StatusValue = GetFieldStringValue(data, Design.StatusField),
            SortIndex = GetSortIndexValue(data),
        };

        private static string GetFieldStringValue(Module data, string fieldName)
        {
            var selectField = data.GetField<SelectField>(fieldName);
            if (selectField != null) return selectField.Value ?? string.Empty;

            var textField = data.GetField<TextField>(fieldName);
            return textField?.Value ?? string.Empty;
        }

        private static async Task SetFieldStringValueAsync(Module data, string fieldName, string value)
        {
            var selectField = data.GetField<SelectField>(fieldName);
            if (selectField != null)
            {
                await selectField.SetValueAsync(value);
                return;
            }
            var textField = data.GetField<TextField>(fieldName);
            if (textField != null) await textField.SetValueAsync(value);
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
}
