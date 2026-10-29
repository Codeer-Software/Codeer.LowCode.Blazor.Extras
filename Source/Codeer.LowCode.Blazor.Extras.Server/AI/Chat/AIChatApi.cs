using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// 共通の API で受ける AI チャットの操作 (AIChatField の送信・返事の状態・中断)。中身は <see cref="AIChatService"/>。
    /// ジョブと会話の所有者は現在のユーザー (ExtrasApiContext.UserId)。他人のジョブは見えない。
    /// 監査ログには誰がどの Agent に送ったかが残る (発言と、Agent が読んだ行は残らない)。
    /// </summary>
    public sealed class AIChatApi : IExtrasApiHandler
    {
        readonly AIChatService _service;

        /// <param name="service">プロセスに 1 つの AIChatService (テンプレの AI/AIChatAgentTable.Service)。</param>
        public AIChatApi(AIChatService service) => _service = service;

        public string Feature => AIChatApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            AIChatApiNames.Send => AuditCategory.DataRead,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            //ジョブと会話の所有者の鍵。空のまま動かすと他人のジョブが見える
            if (string.IsNullOrEmpty(context.UserId)) throw new InvalidOperationException("ExtrasApiContext.UserId is required for AI chat.");
            switch (action)
            {
                case AIChatApiNames.Send:
                    return new AIChatSendResponse { RequestId = await _service.StartAsync(context.UserId, ExtrasApi.ReadPayload<AIChatSendRequest>(payload), context.ModuleDataIO) };

                //知らない Id・結果を忘れた Id・他のユーザーのジョブは応答なし
                case AIChatApiNames.Status:
                    return _service.GetStatus(context.UserId, ExtrasApi.ReadPayload<AIChatJobRequest>(payload).RequestId);

                case AIChatApiNames.Cancel:
                    _service.Cancel(context.UserId, ExtrasApi.ReadPayload<AIChatJobRequest>(payload).RequestId);
                    return null;

                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }
    }
}
