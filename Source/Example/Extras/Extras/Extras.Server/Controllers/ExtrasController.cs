using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Extras.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Extras.Server.Controllers
{
    //Extras の機能 (メール・ジョブ・承認・AI チャット・意味検索の再索引・Excel → PDF・AI 文書解析) の受け口。1 本の POST で受け、要求の Feature / Action で機能に振り分ける。
    //出す機能と機能に渡すものは Services/ExtrasApiTable.cs。監査ログの分類・操作名・試行の記録は振り分け先 (ExtrasApi) が要求を読んでから行う
    [ApiController]
    [Route("api/extras")]
    public class ExtrasController : ControllerBase, IAsyncDisposable
    {
        static readonly ExtrasApi _api = ExtrasApiTable.Create();

        readonly DataService _dataService;
        readonly ILogger<ExtrasController> _logger;

        public ExtrasController(DataService dataService, ILogger<ExtrasController> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        [HttpPost]
        public async Task<ActionResult<ExtrasResponse>> ExecuteAsync(ExtrasRequest request)
        {
            var response = await _api.ExecuteAsync(request, new ExtrasApiContext
            {
                ModuleDataIO = _dataService.ModuleDataIO,
                DesignData = _dataService.Design.DesignData,
                DbAccess = _dataService.DbAccess,
                UserId = await _dataService.GetCurrentUserIdAsync(),
                Logger = _logger,
            });
            return response == null ? NotFound() : response;
        }
    }
}
