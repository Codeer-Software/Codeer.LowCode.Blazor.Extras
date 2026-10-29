using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 共通の API で受けるメールの操作 (MailField / BulkMailField / Mail スクリプトオブジェクトの送信とプレビュー)。
    /// 中身は MailDispatcher / MailBulkSearch / MailPreviewBuilder。自分の Controller で受けるときも同じクラスを同じように呼ぶ。
    /// </summary>
    public sealed class MailApi : IExtrasApiHandler
    {
        readonly MailConfig _mail;
        readonly Func<string, IMailSender?> _createSender;

        /// <param name="mail">appsettings の Mail。</param>
        /// <param name="createSender">送信先の呼び名 → 送信インフラ (テンプレの MailSenderTable.Create)。</param>
        public MailApi(MailConfig mail, Func<string, IMailSender?> createSender)
        {
            _mail = mail;
            _createSender = createSender;
        }

        public string Feature => MailApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            MailApiNames.Send or MailApiNames.SendBulkSearch => AuditCategory.Export,
            MailApiNames.Preview or MailApiNames.PreviewBulkSearch => AuditCategory.DataRead,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            switch (action)
            {
                case MailApiNames.Send:
                    return await CreateDispatcher(context).SendAsync(ExtrasApi.ReadPayload<MailSendRequest>(payload), context.ModuleDataIO);

                //一斉送信 (宛先はサーバーで検索条件から解決。読み取り権限が効き、宛先一覧はクライアントに渡らない)
                case MailApiNames.SendBulkSearch:
                    return await new MailBulkSearch(CreateDispatcher(context), context.ModuleDataIO, context.DesignData, e => LogError(context, e))
                        .SendAsync(ExtrasApi.ReadPayload<MailBulkSearchRequest>(payload));

                //プレビュー (送らずに解決結果を HTML で返す。宛先の解決は送信と同じ経路)
                case MailApiNames.Preview:
                    return await CreatePreviewBuilder(context).BuildSingleHtmlAsync(ExtrasApi.ReadPayload<MailPreviewRequest>(payload));

                case MailApiNames.PreviewBulkSearch:
                    return await CreatePreviewBuilder(context).BuildBulkHtmlAsync(ExtrasApi.ReadPayload<MailBulkSearchRequest>(payload));

                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }

        MailPreviewBuilder CreatePreviewBuilder(ExtrasApiContext context)
            => new(CreateDispatcher(context), context.ModuleDataIO, context.DesignData);

        MailDispatcher CreateDispatcher(ExtrasApiContext context)
        {
            //履歴はシステムの記録なので、操作ユーザーの書き込み権限に依存しない口で書く
            var historyWriter = string.IsNullOrEmpty(_mail.HistoryModuleName)
                ? null
                : new MailHistoryWriter(_mail.HistoryModuleName, context.DesignData,
                    data => context.ModuleDataIO.CreateSystemAccess().AddAsync(data), e => LogError(context, e));
            return new MailDispatcher(_mail, _createSender, historyWriter);
        }

        static void LogError(ExtrasApiContext context, string message)
            => context.Logger?.LogError("{Error}", message);
    }
}
