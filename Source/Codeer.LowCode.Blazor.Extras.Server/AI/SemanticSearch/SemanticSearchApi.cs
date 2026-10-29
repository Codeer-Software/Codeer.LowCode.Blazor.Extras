using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// 共通の API で受ける意味検索の再索引 (SemanticSearchField のスクリプト Reindex / ReindexMissing / CancelReindex)。中身は <see cref="SemanticSearchService"/>。
    /// ジョブの所有者は現在のユーザー (ExtrasApiContext.UserId)。他人のジョブは見えない (同じモジュールの走行中ジョブに合流した人は見える)。
    /// </summary>
    public sealed class SemanticSearchApi : IExtrasApiHandler
    {
        readonly SemanticSearchService _service;
        readonly Func<string, SemanticSearchReindexScope> _openScope;

        /// <param name="service">プロセスに 1 つの SemanticSearchService (テンプレの Extras/SemanticSearchTable.Service)。</param>
        /// <param name="openScope">
        /// ユーザー Id → バックグラウンドの再索引が使う実行ユーザーの ModuleDataIO と DB 接続。
        /// リクエストの外で動くので、ホストはユーザー Id を固定して開く (テンプレは new DataService(userId))。
        /// </param>
        public SemanticSearchApi(SemanticSearchService service, Func<string, SemanticSearchReindexScope> openScope)
        {
            _service = service;
            _openScope = openScope;
        }

        public string Feature => SemanticSearchApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            SemanticSearchApiNames.Reindex => AuditCategory.Admin,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            //ジョブの所有者の鍵。空のまま動かすと他人のジョブが見える
            if (string.IsNullOrEmpty(context.UserId)) throw new InvalidOperationException("ExtrasApiContext.UserId is required for reindex.");
            switch (action)
            {
                case SemanticSearchApiNames.Reindex:
                {
                    var userId = context.UserId;
                    var requestId = await _service.StartReindexAsync(userId, ExtrasApi.ReadPayload<SemanticSearchReindexRequest>(payload), context.ModuleDataIO, () => _openScope(userId));
                    return new SemanticSearchReindexResponse { RequestId = requestId };
                }

                //知らない Id・結果を忘れた Id・他のユーザーのジョブは応答なし
                case SemanticSearchApiNames.Status:
                    return _service.GetReindexStatus(context.UserId, ExtrasApi.ReadPayload<SemanticSearchJobRequest>(payload).RequestId);

                case SemanticSearchApiNames.Cancel:
                    _service.CancelReindex(context.UserId, ExtrasApi.ReadPayload<SemanticSearchJobRequest>(payload).RequestId);
                    return null;

                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }
    }
}
