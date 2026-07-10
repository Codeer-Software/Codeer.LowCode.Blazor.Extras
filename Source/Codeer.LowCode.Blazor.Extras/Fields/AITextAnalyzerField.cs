using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class AITextAnalyzerField(AITextAnalyzerFieldDesign design)
        : FieldBase<AITextAnalyzerFieldDesign>(design)
    {
        /// <summary>
        /// Analysis endpoint for files. URLs belong to the app (which owns the controllers),
        /// so set this once at startup (e.g. in ServiceInitializer).
        /// </summary>
        [ScriptHide]
        public static string FileToModuleDataEndPoint { get; set; } = string.Empty;

        /// <summary>
        /// Analysis endpoint for free text. URLs belong to the app (which owns the controllers),
        /// so set this once at startup (e.g. in ServiceInitializer).
        /// </summary>
        [ScriptHide]
        public static string TextToModuleDataEndPoint { get; set; } = string.Empty;

        /// <summary>
        /// Host-side hook. When set (e.g. desktop apps), the file is analyzed directly
        /// without going through the FileToModuleDataEndPoint.
        /// </summary>
        [ScriptHide]
        public static Func<AITextAnalyzerField, string, StreamContent, Task<ModuleData?>>? FileToModuleDataCoreAsync { get; set; }

        /// <summary>
        /// Host-side hook. When set (e.g. desktop apps), the text is analyzed directly
        /// without going through the TextToModuleDataEndPoint.
        /// </summary>
        [ScriptHide]
        public static Func<AITextAnalyzerField, string, Task<ModuleData?>>? TextToModuleDataCoreAsync { get; set; }

        [ScriptHide]
        public Func<Task> OnDataImportCompletedAsync { get; set; } = async () => await Task.CompletedTask;

        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public async Task SetDataByFileAsync(string fileName, StreamContent content)
        {
            var file = Module.GetField<FileField>(Design.FileField);
            if (file != null)
            {
                await file.SetFileAsync(fileName, content);
            }

            var mod = await FileToModuleDataAsync(fileName, content);
            if (mod == null) return;
            await Module.SetDataAsync(mod);

            await Module!.ExecuteScriptAsync(Design.DataImportCompleted);
            await OnDataImportCompletedAsync();
        }

        [ScriptHide]
        public async Task SetDataByTextAsync(string text)
        {
            var mod = await TextToModuleDataAsync(text);
            if (mod == null) return;
            await Module.SetDataAsync(mod);

            await Module!.ExecuteScriptAsync(Design.DataImportCompleted);
            await OnDataImportCompletedAsync();
        }

        //ホスト(デザイナ等)には登録されていないことがあるため任意解決
        IHttpService? Http => Services.Provider.GetService<IHttpService>();

        async Task<ModuleData?> FileToModuleDataAsync(string fileName, StreamContent content)
        {
            if (FileToModuleDataCoreAsync != null) return await FileToModuleDataCoreAsync(this, fileName, content);
            var endPoint = FileToModuleDataEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return null;
            return await Http.PostContentAsJsonAsync<ModuleData>(
                $"{endPoint}?moduleName={Module.Design.Name}&fieldName={Design.Name}&fileName={fileName}", content);
        }

        async Task<ModuleData?> TextToModuleDataAsync(string text)
        {
            if (TextToModuleDataCoreAsync != null) return await TextToModuleDataCoreAsync(this, text);
            var endPoint = TextToModuleDataEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return null;
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "text", text } });
            var ret = await Http.PostAsync($"{endPoint}?moduleName={Module.Design.Name}&fieldName={Design.Name}", content);
            if (ret?.IsSuccessStatusCode != true) return null;
            return JsonConverterEx.DeserializeObject<ModuleData>(await ret.Content.ReadAsStringAsync());
        }
    }
}
