using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class RichTextField(RichTextFieldDesign design)
        : ValueFieldBase<RichTextFieldDesign, RichTextFieldData, string>(design)
    {
        /// <summary>
        /// メンションの候補の問い合わせの差し替え (HTTP を使わないホスト = デスクトップ用)。引数は (フィールド, @ の後ろに打った文字)。
        /// 設定されていなければ共通の口 (<see cref="ExtrasTransport"/> = ホストの 1 本の API の MentionApi) に問い合わせる。
        /// </summary>
        [ScriptHide]
        public static Func<RichTextField, string, Task<MentionCandidatesResponse?>>? MentionCandidatesCoreAsync { get; set; }

        /// <summary>今の値に入っているメンションの人の Id (重複を除き、本文の順)。</summary>
        public string[] MentionedIds => MentionHtml.GetMentionedIds(Value);

        /// <summary>今の値に「全員」のメンションが入っているか。</summary>
        public bool MentionsAll => MentionHtml.MentionsAll(Value);

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && string.IsNullOrWhiteSpace(Value))
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return await base.ValidateInput();
        }

        /// <summary>
        /// 打った文字に合うメンションの候補をサーバーに問い合わせる。メンションを使わない設定・デザインモード・送る先が無い・失敗のときは空
        /// (打つたびに呼ぶので、失敗は通知しない)。
        /// </summary>
        [ScriptHide]
        public async Task<List<MentionCandidate>> GetMentionCandidatesAsync(string text)
        {
            if (!Design.EnableMention || Services.AppInfoService.IsDesignMode) return new();
            text ??= string.Empty;
            if (MentionCandidatesCoreAsync != null) return (await MentionCandidatesCoreAsync(this, text))?.Candidates ?? new();
            var http = Services.Provider.GetService<IHttpService>();
            if (!ExtrasTransport.CanSend(http)) return new();
            var request = new MentionCandidatesRequest { ModuleName = Module.Design.Name, FieldName = Design.Name, Text = text };
            var response = await ExtrasTransport.CallAsync<MentionCandidatesResponse>(http, MentionApiNames.Feature, MentionApiNames.Candidates, request,
                loading: false, onError: (_, _) => { });
            return response.Response?.Candidates ?? new();
        }
    }
}
