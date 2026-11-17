using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Properties;

namespace Codeer.LowCode.Blazor.Extras.Server.Mention
{
    /// <summary>
    /// 共通の API で受ける、RichTextField のメンションの候補 (Mention.Candidates)。
    /// 入口で、問い合わせた人がそのフィールドを見られること (本体の CheckFieldUserReadAsync) と、そのフィールドがメンションを使う RichTextField であることを確かめる。
    /// 候補の決め方は <see cref="MentionCandidateQuery"/>。
    /// </summary>
    public sealed class MentionApi : IExtrasApiHandler
    {
        public string Feature => MentionApiNames.Feature;

        //打つたびに問い合わせるので、失敗と拒否だけ残す
        public AuditCategory GetAuditCategory(string action) => AuditCategory.Other;

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            if (action != MentionApiNames.Candidates) throw ExtrasApi.UnknownAction(Feature, action);
            var request = ExtrasApi.ReadPayload<MentionCandidatesRequest>(payload);
            var design = await FieldApiAuthorization.CheckAsync<RichTextFieldDesign>(context.ModuleDataIO, request.ModuleName, request.FieldName, Resources.RichTextMention_NotFound);
            if (!design.EnableMention) throw LowCodeException.Create(Resources.RichTextMention_NotFound, request.ModuleName, request.FieldName);
            return new MentionCandidatesResponse
            {
                Candidates = await MentionCandidateQuery.FindAsync(context.ModuleDataIO, context.DesignData, request.Text, context.Logger),
            };
        }
    }
}
