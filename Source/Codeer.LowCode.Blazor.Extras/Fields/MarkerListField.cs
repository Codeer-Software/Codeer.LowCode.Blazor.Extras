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
    public class Marker
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public Module? Module { get; set; }
    }

    public class MarkerListField(MarkerListFieldDesign design)
        : FieldBase<MarkerListFieldDesign>(design), ISearchResultsViewField
    {
        private SearchCondition? _additionalCondition;

        internal string ObjectUrl { get; set; } = string.Empty;
        internal string ImageExtension => Design.ResourcePath.Split('.').LastOrDefault() ?? string.Empty;
        internal List<Marker> MarkerList { get; set; } = new();

        [ScriptHide]
        public string ModuleName => Design?.SearchCondition.ModuleName ?? string.Empty;

        public override bool IsModified => false;
        public override FieldDataBase? GetData() => null;
        public override FieldSubmitData GetSubmitData() => new();
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            await LoadImage();
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
            await Task.CompletedTask;
            var count = MarkerList.Count;
            MarkerList.RemoveAll(e => e.Module?.IsDeleted == true);
            if (count == MarkerList.Count) return;

            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            var modules = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.DetailLayoutName);
            MarkerList.Clear();
            MarkerList.AddRange(modules.Select(ConvertToMarker));
            NotifyStateChanged();
        }

        internal async Task AddAsync(int x, int y)
        {
            var mod = await this.CreateChildModuleAsync(ModuleName, ModuleLayoutType.Detail, Design.DetailLayoutName);

            await mod.AssignRequiredCondition(Design.SearchCondition);

            SetNumberFieldValue(mod, Design.XField, x);
            SetNumberFieldValue(mod, Design.YField, y);

            if (await mod.ShowDialogAsync(Properties.Resources.OK, Properties.Resources.Cancel) != Properties.Resources.OK) return;
            if (!mod.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InputError);
                return;
            }
            if (await mod.SubmitAsync() != true) return;

            await ReloadAsync();
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
            }
            else if (dialogResult == Properties.Resources.Delete)
            {
                await mod.DeleteAsync();
            }
            else
            {
                return;
            }

            await ReloadAsync();
            await InvokeOnDataChangedAsync();
            await NotifyDataChangedAsync();
        }

        private async Task InvokeOnDataChangedAsync()
        {
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
        }

        private Marker ConvertToMarker(Module data)
        {
            return new Marker
            {
                Id = data.GetIdText(),
                Label = string.IsNullOrEmpty(Design.LabelField)
                    ? string.Empty
                    : data.GetField<TextField>(Design.LabelField)?.Value ?? string.Empty,
                X = (int)(data.GetField<NumberField>(Design.XField)?.Value ?? 0),
                Y = (int)(data.GetField<NumberField>(Design.YField)?.Value ?? 0),
                Module = data,
            };
        }

        private SearchCondition GetSearchCondition()
            => Design.SearchCondition.MergeSearchCondition(_additionalCondition);

        private async Task LoadImage()
        {
            if (string.IsNullOrEmpty(Design.ResourcePath)) return;

            using var memoryStream = await Services.AppInfoService.GetResourceAsync(Design.ResourcePath);
            if (memoryStream == null) return;

            var bin = memoryStream!.ToArray();
            ObjectUrl = Convert.ToBase64String(bin);
        }

        private static void SetNumberFieldValue(Module mod, string fieldName, int value)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            var field = mod.GetField<NumberField>(fieldName);
            field?.SetValueAsync((decimal)value).GetAwaiter().GetResult();
        }
    }
}
