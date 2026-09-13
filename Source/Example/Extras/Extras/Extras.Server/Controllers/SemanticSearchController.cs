using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Extras.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Extras.Server.Controllers
{
    /// <summary>
    /// SemanticSearchField (意味検索) の再索引 API。
    /// フィールドを後から置いたとき・埋め込みモデルを変えたとき・埋め込みに失敗した行を埋めるときに、モジュールの全行の文章とベクトルを作り直す。
    /// 行は実行ユーザーの ModuleDataIO で読み、通常の Submit で書く (読み書きの権限は通常どおり効く。埋め込みは CustomizedModuleDataIO が付ける)。
    /// 例: POST api/semantic_search/reindex/Inquiry
    /// </summary>
    [ApiController]
    [Route("api/semantic_search")]
    public class SemanticSearchController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;

        public SemanticSearchController(DataService dataService)
            => _dataService = dataService;

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        //戻り値は書き直した行数
        [HttpPost("reindex/{moduleName}")]
        public async Task<ActionResult<int>> Reindex(string moduleName, CancellationToken cancellationToken)
        {
            try
            {
                return await SemanticSearchIndexer.ReindexAsync(_dataService.ModuleDataIO, DesignerService.GetDesignData(), moduleName, cancellationToken: cancellationToken);
            }
            catch (ArgumentException e)
            {
                return BadRequest(e.Message);
            }
        }
    }
}
