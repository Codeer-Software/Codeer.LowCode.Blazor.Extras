using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Design.Samples.AIDocumentAnalyzer;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class AITextAnalyzerField(AITextAnalyzerFieldDesign design)
        : FieldBase<AITextAnalyzerFieldDesign>(design)
    {
        /// <summary>
        /// Host-side hook. When set (e.g. desktop apps), the file is analyzed directly
        /// without going through the AITextAnalyzeFileEndPoint.
        /// </summary>
        [ScriptHide]
        public static Func<AITextAnalyzerField, string, StreamContent, Task<ModuleData?>>? FileToModuleDataCoreAsync { get; set; }

        /// <summary>
        /// Host-side hook. When set (e.g. desktop apps), the text is analyzed directly
        /// without going through the AITextAnalyzeTextEndPoint.
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
        ExtrasClientOptions? Options => Services.Provider.GetService<ExtrasClientOptions>();

        async Task<ModuleData?> FileToModuleDataAsync(string fileName, StreamContent content)
        {
            if (FileToModuleDataCoreAsync != null) return await FileToModuleDataCoreAsync(this, fileName, content);
            var endPoint = Options?.AITextAnalyzeFileEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return null;
            return await Http.PostContentAsJsonAsync<ModuleData>(
                $"{endPoint}?moduleName={Module.Design.Name}&fieldName={Design.Name}&fileName={fileName}", content);
        }

        async Task<ModuleData?> TextToModuleDataAsync(string text)
        {
            if (TextToModuleDataCoreAsync != null) return await TextToModuleDataCoreAsync(this, text);
            var endPoint = Options?.AITextAnalyzeTextEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return null;
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "text", text } });
            var ret = await Http.PostAsync($"{endPoint}?moduleName={Module.Design.Name}&fieldName={Design.Name}", content);
            if (ret?.IsSuccessStatusCode != true) return null;
            return JsonConverterEx.DeserializeObject<ModuleData>(await ret.Content.ReadAsStringAsync());
        }
    }
}
