using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class Marker 
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class MarkerListField : FieldBase<MarkerListFieldDesign>
    {
        ListField? _list;

        internal string ObjectUrl { get; set; } = string.Empty;
        internal string ImageExtension => Design.ResourcePath.Split('.').LastOrDefault()??string.Empty;
        internal List<Marker> MarkerList { get; set; } = new();

        public MarkerListField(MarkerListFieldDesign design) : base(design) { }
        public override bool IsModified => false;
        public override FieldDataBase? GetData() => null;
        public override FieldSubmitData GetSubmitData() => new();
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            await LoadImage();
        }

        internal async Task InitAsync()
        {
            await Task.CompletedTask;
            _list = Module.GetField<ListField>(Design.ListField);

            var js = Module.Services.Provider.GetService<IJSRuntime>();

            if (_list != null)
            {
                _list.OnDataChangedAsync = async () =>
                {
                    InitList();
                    await Task.CompletedTask;
                    NotifyStateChanged();
                };
            }
            InitList();
        }

        internal async Task OnDoubleClickPointAsync(int x, int y)
            => await Module!.ExecuteScriptAsync(Design.OnDoubleClickPoint, x, y);

        internal async Task OnClickMarkerAsync(string id)
            => await Module!.ExecuteScriptAsync(Design.OnClickMarker, id);

        void InitList()
        {
            if (Module.Services.AppInfoService.IsDesignMode) return;

            MarkerList = new();
            if (_list == null) return;

            foreach (var e in _list.Rows)
            {
                var x = e.GetFields();

                var marker = new Marker();
                marker.Id = e.GetField<IdField>("Id")?.Value ?? string.Empty;
                marker.Label = e.GetField<TextField>(Design.LabelFieldOfListField)?.Value ?? string.Empty;
                marker.X = (int)(e.GetField<NumberField>("X")?.Value ?? 0);
                marker.Y = (int)(e.GetField<NumberField>("Y")?.Value ?? 0);
                MarkerList.Add(marker);
            }
        }

        async Task LoadImage()
        {
            if (string.IsNullOrEmpty(Design.ResourcePath)) return;

            using var memoryStream = await Services.AppInfoService.GetResourceAsync(Design.ResourcePath);
            if (memoryStream == null) return;

            var bin = memoryStream!.ToArray();
            ObjectUrl = Convert.ToBase64String(bin);
        }
    }
}
