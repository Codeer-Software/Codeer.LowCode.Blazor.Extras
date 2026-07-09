using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class AITextAnalyzerCore : IAITextAnalyzerCore
    {
        readonly IHttpService _httpService;
        readonly ExtrasClientOptions _options;

        public AITextAnalyzerCore(IHttpService httpService, ExtrasClientOptions options)
        {
            _httpService = httpService;
            _options = options;
        }

        public virtual async Task<ModuleData?> FileToModuleDataAsync(string moduleName, string fieldName, string fileName, StreamContent content)
        {
            if (string.IsNullOrEmpty(_options.AITextAnalyzeFileEndPoint)) return null;
            return await _httpService.PostContentAsJsonAsync<ModuleData>(
                $"{_options.AITextAnalyzeFileEndPoint}?moduleName={moduleName}&fieldName={fieldName}&fileName={fileName}", content);
        }

        public virtual async Task<ModuleData?> TextToModuleDataAsync(string moduleName, string fieldName, string text)
        {
            if (string.IsNullOrEmpty(_options.AITextAnalyzeTextEndPoint)) return null;
            var content = new FormUrlEncodedContent(new Dictionary<string, string> { { "text", text } });
            var ret = await _httpService.PostAsync($"{_options.AITextAnalyzeTextEndPoint}?moduleName={moduleName}&fieldName={fieldName}", content);
            if (ret?.IsSuccessStatusCode != true) return null;
            return JsonConverterEx.DeserializeObject<ModuleData>(await ret.Content.ReadAsStringAsync());
        }
    }
}
