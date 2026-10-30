using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.AI
{
    /// <summary>
    /// 共通の API で受ける AI 文書解析 (AITextAnalyzerField のファイル / 文章 → モジュールのデータ)。中身は <see cref="AITextAnalyzeService"/>。
    /// 権限・デザインの拒否 (LowCodeException) はそのまま返し、モデル側の失敗は「再試行で直ることがある」の 1 文にまとめる。
    /// </summary>
    public sealed class AITextAnalyzeApi : IExtrasApiHandler
    {
        readonly AITextAnalyzeService _service;

        /// <param name="settings">appsettings の AISettings (Azure OpenAI / Document Intelligence)。</param>
        public AITextAnalyzeApi(AISettings settings) : this(new AITextAnalyzeService(settings)) { }

        /// <param name="service">抽出に使う IChatClient をホストが決めるときは AITextAnalyzeService(AISettings, Func&lt;IChatClient&gt;) を渡す。</param>
        public AITextAnalyzeApi(AITextAnalyzeService service) => _service = service;

        public string Feature => AITextAnalyzeApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => AuditCategory.Other;

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            //操作と要求の読み取りは解析の外 (知らない操作・壊れた要求はそのまま返す)
            Func<Task<object?>> analyze = action switch
            {
                AITextAnalyzeApiNames.AnalyzeFile => () => AnalyzeFileAsync(ExtrasApi.ReadPayload<AITextAnalyzeFileRequest>(payload), context),
                AITextAnalyzeApiNames.AnalyzeText => () => AnalyzeTextAsync(ExtrasApi.ReadPayload<AITextAnalyzeTextRequest>(payload), context),
                _ => throw ExtrasApi.UnknownAction(Feature, action),
            };
            try
            {
                return await analyze();
            }
            catch (LowCodeException)
            {
                throw;
            }
            catch
            {
                //モデル側・ネットワークの失敗の生の文言は返さない
                throw new Exception("AI analysis failed. Retrying may succeed.");
            }
        }

        async Task<object?> AnalyzeFileAsync(AITextAnalyzeFileRequest request, ExtrasApiContext context)
        {
            using var content = new MemoryStream(request.Content);
            return await _service.AnalyzeFileAsync(context.ModuleDataIO, context.DesignData.Modules, request.ModuleName, request.FieldName, request.FileName, content);
        }

        async Task<object?> AnalyzeTextAsync(AITextAnalyzeTextRequest request, ExtrasApiContext context)
            => await _service.AnalyzeTextAsync(context.ModuleDataIO, context.DesignData.Modules, request.ModuleName, request.FieldName, request.Text);
    }
}
