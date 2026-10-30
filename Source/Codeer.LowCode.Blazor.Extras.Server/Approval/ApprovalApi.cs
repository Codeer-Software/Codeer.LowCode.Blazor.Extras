using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Approval
{
    /// <summary>
    /// 共通の API で受ける承認フローの操作 (ApprovalFlowField の申請/再申請/承認/却下/差し戻し/取り下げ/確認)。
    /// 中身は <see cref="ApprovalEngine"/>。順番到達の通知メールはメンバー契約の TurnNotifyMail が設定されているときだけ送られる。
    /// </summary>
    public sealed class ApprovalApi : IExtrasApiHandler
    {
        readonly MailConfig _mail;
        readonly Func<string, IMailSender?> _createSender;

        /// <param name="mail">appsettings の Mail (通知メールの送信元と履歴モジュール)。</param>
        /// <param name="createSender">送信先の呼び名 → 送信インフラ (テンプレの MailSenderTable.Create)。</param>
        public ApprovalApi(MailConfig mail, Func<string, IMailSender?> createSender)
        {
            _mail = mail;
            _createSender = createSender;
        }

        public string Feature => ApprovalApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            ApprovalApiNames.Execute => AuditCategory.DataWrite,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            switch (action)
            {
                case ApprovalApiNames.Execute:
                {
                    var command = ExtrasApi.ReadPayload<ApprovalCommand>(payload);
                    var result = await CreateEngine(context).ExecuteAsync(command);
                    context.Audit?.AddTarget(command.TargetModuleName, result.TargetId, $"Approval:{command.Action}");
                    if (!string.IsNullOrEmpty(result.ErrorMessage)) context.Audit?.Fail(result.ErrorMessage);
                    return result;
                }
                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }

        ApprovalEngine CreateEngine(ExtrasApiContext context)
        {
            //承認の記録と通知メールの履歴はシステムの記録なので、操作ユーザーの書き込み権限に依存しない口で書く
            var system = context.ModuleDataIO.CreateSystemAccess();
            var historyWriter = string.IsNullOrEmpty(_mail.HistoryModuleName)
                ? null
                : new MailHistoryWriter(_mail.HistoryModuleName, context.DesignData, data => system.AddAsync(data), e => LogError(context, e));
            return new ApprovalEngine(context.DesignData, context.ModuleDataIO, context.DbAccess, data => system.AddAsync(data), data => system.UpdateAsync(data))
            {
                MailDispatcher = new MailDispatcher(_mail, _createSender, historyWriter),
                LogError = e => LogError(context, e),
            };
        }

        static void LogError(ExtrasApiContext context, string message)
            => context.Logger?.LogError("{Error}", message);
    }
}
