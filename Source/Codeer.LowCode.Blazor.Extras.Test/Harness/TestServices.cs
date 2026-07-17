using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.Harness
{
    /// <summary>
    /// コードで組んだ DesignData から実 Module を生成して Field の挙動をテストするための最小ハーネス。
    /// DB には接続しない (ModuleLayoutType.None で生成すればリスト系のロードも走らない)。
    /// コアの public API のみで構成 (Module 生成は ModuleCreationService.CreateModuleAsync)。
    /// </summary>
    public class TestServices
    {
        public Codeer.LowCode.Blazor.RequestInterfaces.Services Core { get; }
        public HarnessAppService App { get; }
        public ListLogger Logger { get; } = new();

        public TestServices(DesignData designData)
        {
            App = new HarnessAppService(designData);
            Core = new Codeer.LowCode.Blazor.RequestInterfaces.Services(new NullServiceProvider(), App, App,
                new DummyNavigationService(), new DummyUIService(), Logger);
        }

        public async Task<Module> CreateModuleAsync(string moduleName,
            ModuleLayoutType layoutType = ModuleLayoutType.None, string layoutName = "")
            => await ModuleCreationService.CreateModuleAsync(Core, new ModuleData { Name = moduleName }, layoutType, layoutName);

        class NullServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }

    /// <summary>IAppInfoService + IModuleDataService の軽量実装。一括ダウンロードに渡された条件をキャプチャする。</summary>
    public class HarnessAppService : IAppInfoService, IModuleDataService
    {
        readonly DesignData _designData;
        readonly ScriptRuntimeTypeManager _scriptRuntimeTypeManager = new();

        public HarnessAppService(DesignData designData)
            => _designData = designData;

        //IAppInfoService
        public string CurrentUserId => string.Empty;
        public ModuleData? CurrentUserData { get; set; }
        public ScriptRuntimeTypeManager GetScriptRuntimeTypeManager() => _scriptRuntimeTypeManager;
        public Task<MemoryStream?> GetResourceAsync(string resourcePath) => Task.FromResult<MemoryStream?>(null);
        public DesignData GetDesignData() => _designData;

        //IModuleDataService (テストが使わないものは未実装)
        public Task<List<Paging<ModuleData>>> GetListAsync(List<GetListRequest> requests)
            => Task.FromResult(requests.Select(_ => new Paging<ModuleData>()).ToList());
        public Task<List<ModuleSubmitResult>?> SubmitAsync(List<ModuleSubmitData> data)
            => throw new NotImplementedException();
        public Task<Codeer.LowCode.Blazor.DataIO.FileInfo?> UploadFile(string moduleName, string fieldName, string fileName, StreamContent content)
            => throw new NotImplementedException();
        public Task<MemoryStream?> DownloadFile(string moduleName, string fieldName, string id)
            => throw new NotImplementedException();

        /// <summary>一括ダウンロード (list_file) に渡された条件。</summary>
        public SearchCondition? LastListFileCondition { get; private set; }

        public Task<MemoryStream?> GetListFileAsync(SearchCondition condition)
        {
            LastListFileCondition = condition;
            return Task.FromResult<MemoryStream?>(new MemoryStream());
        }

        public Task<List<ModuleSubmitResult>?> SubmitByFileAsync(string moduleName, StreamContent content)
            => throw new NotImplementedException();
    }

    public class ListLogger : ILogger
    {
        public List<string> ErrorList { get; } = new();
        public List<string> LogList { get; } = new();
        public List<string> WarnList { get; } = new();

        public Task Error(string message) { ErrorList.Add(message); return Task.CompletedTask; }
        public Task Log(string message) { LogList.Add(message); return Task.CompletedTask; }
        public Task Warn(string message) { WarnList.Add(message); return Task.CompletedTask; }
    }

    public class DummyNavigationService : INavigationService
    {
        public string? AppName => throw new NotImplementedException();
        public bool CanLogout => throw new NotImplementedException();
        public string GetAppRootUrl() => throw new NotImplementedException();
        public string GetAppUrl() => throw new NotImplementedException();
        public string GetModuleDataUrl(string module, string id) => throw new NotImplementedException();
        public string GetModuleDataUrl(string pageFrame, string module, string id) => throw new NotImplementedException();
        public string GetModuleDataUrl(string app, string pageFrame, string module, string id) => throw new NotImplementedException();
        public string GetModuleDesignerUrl(string moduleName) => throw new NotImplementedException();
        public string GetModuleUrl(string module) => throw new NotImplementedException();
        public string GetModuleUrl(string pageFrame, string module) => throw new NotImplementedException();
        public string GetModuleUrl(string app, string pageFrame, string module) => throw new NotImplementedException();
        public string GetListUrl(string app, string pageFrame, string module) => throw new NotImplementedException();
        public string GetPreviewModuleUrl(string pageFrame, string moduleName) => throw new NotImplementedException();
        public string GetPreviewListUrl(string pageFrame, string moduleName) => throw new NotImplementedException();
        public string GetPreviewRootUrl() => throw new NotImplementedException();
        public string GetTopPageUrl() => throw new NotImplementedException();
        public string GetUrl(PageLink pageLink) => throw new NotImplementedException();
        public void NavigateToModule(string module) { }
        public void NavigateToModule(string pageFrame, string module) { }
        public void NavigateToModule(string app, string pageFrame, string module) { }
        public void NavigateToModuleData(string module, string id) { }
        public void NavigateToModuleData(string pageFrame, string module, string id) { }
        public void NavigateToModuleData(string app, string pageFrame, string module, string id) { }
        public void NavigateToList(string app, string pageFrame, string module) { }
        public void ReplaceToModule(string pageFrame, string module) { }
        public void ReplaceToModule(string app, string pageFrame, string module) { }
        public void ReplaceToModuleData(string module, string id) { }
        public void ReplaceToModuleDesign(string module) { }
        public void ReplaceToList(string pageFrame, string module) { }
        public void ReplaceToList(string app, string pageFrame, string module) { }
        public Task Logout() => throw new NotImplementedException();
        public void NavigateTo(string url) => throw new NotImplementedException();
        public void ReplaceTo(string url) => throw new NotImplementedException();
        public Dictionary<string, List<string>> GetQueryParameters() => new();
    }

    public class DummyUIService : IUIService
    {
        public List<(MemoryStream Stream, string Name)> Downloads { get; } = new();

        public Task CloseDialog(Module moduleData, string button) => Task.CompletedTask;
        public Task ClosePanel(Module module, string button) => Task.CompletedTask;
        public Task ClosePopup(Module module, string button) => Task.CompletedTask;
        public Task<bool> DownloadFile(MemoryStream stream, string name)
        {
            Downloads.Add((stream, name));
            return Task.FromResult(true);
        }
        public Task NotifySuccess(string message) => Task.CompletedTask;
        public Task NotifyError(string message) => Task.CompletedTask;
        public Task<string> ShowDialog(Module moduleData, DialogButton[] buttons) => Task.FromResult(string.Empty);
        public Task<string> ShowPopup(Module moduleData, int x, int y, DialogButton[] buttons) => Task.FromResult(string.Empty);
        public Task<string> ShowPanel(Module moduleData, DialogButton[] buttons, PanelAlignment alignment) => Task.FromResult(string.Empty);
        public Task<string> ShowMessageBox(string title, string message, DialogButton[] buttons) => Task.FromResult(string.Empty);
    }
}
