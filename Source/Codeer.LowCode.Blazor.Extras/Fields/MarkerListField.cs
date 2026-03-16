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
        private readonly ModuleCollection _modules = new();
        private SearchCondition? _additionalCondition;

        [ScriptHide]
        public Func<Task> OnDataChangedAsync { get; set; } = () => Task.CompletedTask;

        internal string ObjectUrl { get; set; } = string.Empty;
        internal string ImageExtension { get; set; } = string.Empty;
        internal List<Marker> MarkerList { get; set; } = new();

        [ScriptHide]
        public string ModuleName => Design?.SearchCondition.ModuleName ?? string.Empty;

        public override bool IsModified => _modules.IsModified;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
            => _modules.GetSubmitData(Services.AppInfoService,
                SearchConditionHelper.ResolveSearchCondition(Services.AppInfoService, Design.SearchCondition, Module?.GetData()));

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            await LoadImageAsync();
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

            if (fieldName == Design.ImageFileField)
            {
                await LoadImageFromFileFieldAsync();
                NotifyStateChanged();
                return;
            }

            var searchCondition = GetSearchCondition();
            if (searchCondition.GetFieldVariableConditions().All(e => new VariableName(e.Variable).FieldName.Root != fieldName)) return;
            await ReloadAsync();
        }

        [ScriptHide]
        public override async Task OnChildDataChangedAsync()
        {
            MarkerList.RemoveAll(e => e.Module?.IsDeleted == true);
            await InvokeOnDataChangedAsync();
        }

        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            var modules = await this.GetChildModulesAsync(GetSearchCondition(), ModuleLayoutType.Detail, Design.DetailLayoutName);
            _modules.ApplyLoaded(modules);
            MarkerList.Clear();
            MarkerList.AddRange(modules.Select(ConvertToMarker));
            NotifyStateChanged();
        }

        internal async Task AddAsync(int x, int y)
        {
            if (!string.IsNullOrEmpty(Design.OnDoubleClickPoint))
            {
                await Module.ExecuteScriptAsync(Design.OnDoubleClickPoint, x, y);
                return;
            }

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

            _modules.Add(mod);
            MarkerList.Add(ConvertToMarker(mod));

            await InvokeOnDataChangedAsync();
        }

        internal async Task OnMarkerClickAsync(Marker m, bool viewOnly)
        {
            if (!string.IsNullOrEmpty(Design.OnClickMarker))
            {
                await Module.ExecuteScriptAsync(Design.OnClickMarker, m.Id);
                return;
            }
            await EditAsync(m.Module, viewOnly);
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

                var marker = MarkerList.FirstOrDefault(e => e.Module?.GetIdText() == mod.GetIdText());
                if (marker != null)
                {
                    marker.Label = string.IsNullOrEmpty(Design.LabelField)
                        ? string.Empty
                        : mod.GetField<TextField>(Design.LabelField)?.Value ?? string.Empty;
                    marker.X = (int)(mod.GetField<NumberField>(Design.XField)?.Value ?? 0);
                    marker.Y = (int)(mod.GetField<NumberField>(Design.YField)?.Value ?? 0);
                }
            }
            else if (dialogResult == Properties.Resources.Delete)
            {
                MarkerList.RemoveAll(e => e.Module == mod);
                _modules.Remove(mod);
            }
            else
            {
                return;
            }

            await InvokeOnDataChangedAsync();
        }

        private async Task InvokeOnDataChangedAsync()
        {
            await NotifyDataChangedAsync();
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            await OnDataChangedAsync();
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

        private async Task LoadImageAsync()
        {
            if (!string.IsNullOrEmpty(Design.ImageFileField))
            {
                await LoadImageFromFileFieldAsync();
                return;
            }

            if (string.IsNullOrEmpty(Design.ResourcePath)) return;

            using var memoryStream = await Services.AppInfoService.GetResourceAsync(Design.ResourcePath);
            if (memoryStream == null) return;

            var bin = memoryStream!.ToArray();
            ObjectUrl = Convert.ToBase64String(bin);
            ImageExtension = Design.ResourcePath.Split('.').LastOrDefault() ?? string.Empty;
        }

        private async Task LoadImageFromFileFieldAsync()
        {
            var fileField = Module.GetField<FileField>(Design.ImageFileField);
            if (fileField == null) return;

            var stream = await fileField.GetMemoryStreamAsync();
            if (stream == null)
            {
                ObjectUrl = string.Empty;
                ImageExtension = string.Empty;
                return;
            }

            ObjectUrl = Convert.ToBase64String(stream.ToArray());
            ImageExtension = fileField.FileName?.Split('.').LastOrDefault() ?? string.Empty;
        }

        private static void SetNumberFieldValue(Module mod, string fieldName, int value)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            var field = mod.GetField<NumberField>(fieldName);
            field?.SetValueAsync((decimal)value).GetAwaiter().GetResult();
        }
    }
}
