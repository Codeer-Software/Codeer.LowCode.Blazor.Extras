using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class AITextAnalyzerField(AITextAnalyzerFieldDesign design)
        : FieldBase<AITextAnalyzerFieldDesign>(design)
    {
        /// <summary>
        /// ファイル解析専用のエンドポイント (本文を POST)。空 (既定) なら共通の口 (<see cref="ExtrasTransport"/> = ホストの 1 本の API の AITextAnalyzeApi) を使う。
        /// 解析専用の Controller を持つホストが起動時に設定する。
        /// </summary>
        [ScriptHide]
        public static string FileToModuleDataEndPoint { get; set; } = string.Empty;

        /// <summary>
        /// 文章解析専用のエンドポイント (フォームで POST)。空 (既定) なら共通の口 (<see cref="ExtrasTransport"/>) を使う。
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

        //受け付ける拡張子 (input の accept 属性)。accept は補助で、判定は SetDataByFileAsync で行う
        internal string? AcceptAttribute => FileExtensionFilter.ToAccept(Design.GetAllowedExtensions(Module?.Design));

        [ScriptHide]
        public async Task SetDataByFileAsync(string fileName, StreamContent content)
        {
            if (!Design.IsAllowedFileName(Module?.Design, fileName))
                throw LowCodeException.Create(Properties.Resources.FileExtensionNotAllowed, FileExtensionFilter.ToDisplayText(Design.GetAllowedExtensions(Module?.Design)));

            var file = Module!.GetField<FileField>(Design.FileField);
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

        //差し替え (XxxCoreAsync) があればそれ、解析専用のルート (XxxEndPoint) があればそこへ POST、どちらも無ければ共通の口 (ExtrasTransport)
        async Task<ModuleData?> FileToModuleDataAsync(string fileName, StreamContent content)
        {
            if (FileToModuleDataCoreAsync != null) return await FileToModuleDataCoreAsync(this, fileName, content);
            var endPoint = FileToModuleDataEndPoint;
            if (!string.IsNullOrEmpty(endPoint))
            {
                if (Http == null) return null;
                return await Http.PostContentAsJsonAsync<ModuleData>(
                    $"{endPoint}?moduleName={Module.Design.Name}&fieldName={Design.Name}&fileName={fileName}", content);
            }
            if (!ExtrasTransport.CanSend(Http)) return null;
            var request = new AITextAnalyzeFileRequest { ModuleName = Module.Design.Name, FieldName = Design.Name, FileName = fileName, Content = await content.ReadAsByteArrayAsync() };
            return (await ExtrasTransport.CallAsync<ModuleData>(Http, AITextAnalyzeApiNames.Feature, AITextAnalyzeApiNames.AnalyzeFile, request)).Response;
        }

        async Task<ModuleData?> TextToModuleDataAsync(string text)
        {
            if (TextToModuleDataCoreAsync != null) return await TextToModuleDataCoreAsync(this, text);
            var endPoint = TextToModuleDataEndPoint;
            if (!string.IsNullOrEmpty(endPoint))
            {
                if (Http == null) return null;
                var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "text", text } });
                var ret = await Http.PostAsync($"{endPoint}?moduleName={Module.Design.Name}&fieldName={Design.Name}", content);
                if (ret?.IsSuccessStatusCode != true) return null;
                return JsonConverterEx.DeserializeObject<ModuleData>(await ret.Content.ReadAsStringAsync());
            }
            if (!ExtrasTransport.CanSend(Http)) return null;
            var request = new AITextAnalyzeTextRequest { ModuleName = Module.Design.Name, FieldName = Design.Name, Text = text };
            return (await ExtrasTransport.CallAsync<ModuleData>(Http, AITextAnalyzeApiNames.Feature, AITextAnalyzeApiNames.AnalyzeText, request)).Response;
        }
    }
}
