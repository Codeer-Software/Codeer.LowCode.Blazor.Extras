using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 一斉メール送信ボタン。宛先は同一モジュール上のリストフィールドの検索条件から
    /// サーバー側で解決される (全行対象・アドレスはクライアントに渡らない)。
    /// 未保存の変更がある間は送信できない (保存済みの状態=サーバーから見える状態が送信対象)。
    /// </summary>
    public class BulkMailField(BulkMailFieldDesign design)
        : ValueFieldBase<BulkMailFieldDesign, BulkMailFieldData, string>(design)
    {
        bool _isSending;

        /// <summary>送信中か (ボタンの二重実行防止)。</summary>
        [ScriptHide]
        public bool IsSending => _isSending;

        [ScriptHide]
        public override async Task SetValueAsync(string? value)
            => await base.SetValueAsync(value);

        /// <summary>送信結果サマリ (新しい順)。</summary>
        internal List<BulkMailSummaryEntry> GetSummaryEntries() => BulkMailSummary.Parse(Value);

        /// <summary>ボタンからの送信。確認ダイアログを挟み、結果をトーストで知らせる。</summary>
        internal async Task SendFromButtonAsync()
        {
            if (Services.AppInfoService.IsDesignMode) return;
            if (_isSending || Module == null) return;

            //未保存の内容(テンプレ・名簿・条件)で送らない
            if (Module.IsNewData || Module.IsModified)
            {
                await Services.UIService.ShowMessageBox(string.Empty, Properties.Resources.BulkMailSaveBeforeSend,
                    [new DialogButton("btn btn-outline-primary", Properties.Resources.OK)]);
                return;
            }

            var listField = Module.GetField<ListField>(Design.RecipientListFieldName);
            if (listField?.GetSearchCondition() == null || string.IsNullOrEmpty(Design.EmailAddressVariable))
            {
                await Services.UIService.NotifyError(Properties.Resources.BulkMailTargetListInvalid);
                return;
            }

            //確認 (件数はリストの全件数。除外フラグ行はサーバーでスキップされる)
            var message = string.Format(Properties.Resources.BulkMailConfirmFormat, listField.TotalCount);
            var answer = await Services.UIService.ShowMessageBox(string.Empty, message,
                [new DialogButton("btn btn-outline-primary", Properties.Resources.BulkMailSendAction),
                 new DialogButton("btn btn-outline-secondary", Properties.Resources.Cancel)]);
            if (answer != Properties.Resources.BulkMailSendAction) return;

            var result = await SendCoreAsync(listField);
            if (result == null) return;

            if (result.IsSuccess)
            {
                await Services.UIService.NotifySuccess(string.Format(Properties.Resources.BulkMailSentFormat, result.SuccessCount));
            }
            else
            {
                await Services.UIService.NotifyError(string.Format(Properties.Resources.BulkMailSentWithFailuresFormat,
                    result.SuccessCount, result.TotalCount));
            }
        }

        /// <summary>スクリプトからの送信。確認ダイアログ・トーストは出さない (呼び出し側が制御する)。</summary>
        [ScriptName("Send")]
        public async Task<MailSendResult> SendAsync()
        {
            if (Module == null || _isSending)
                return MailSendResult.Failure(string.Empty, "The field is not ready to send.");
            if (Module.IsNewData || Module.IsModified)
                return MailSendResult.Failure(string.Empty, "Save the record before sending.");

            var listField = Module.GetField<ListField>(Design.RecipientListFieldName);
            if (listField?.GetSearchCondition() == null || string.IsNullOrEmpty(Design.EmailAddressVariable))
                return MailSendResult.Failure(string.Empty, "The recipient list or the to-address variable is not configured.");

            return await SendCoreAsync(listField) ?? MailSendResult.Failure(string.Empty, "Sending is already in progress.");
        }

        async Task<MailSendResult?> SendCoreAsync(ListField listField)
        {
            if (_isSending) return null;
            _isSending = true;
            NotifyStateChanged();
            try
            {
                //リストの検索条件に合致する全行を対象にする(表示中のページ・列に縛られない)
                var condition = listField.GetSearchCondition()!;
                condition.LimitCount = null;
                condition.SelectFields = new(); //必要列はサーバーがテンプレ変数から組み直す

                var subject = ResolveTemplate(Design.SubjectVariable, Design.Subject);
                var body = ResolveTemplate(Design.BodyVariable, Design.Body);

                var request = new MailBulkSearchRequest
                {
                    SenderName = Design.SenderName,
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = Design.IsBodyHtml,
                    ReplyTo = Design.ReplyTo,
                    Condition = condition,
                    EmailAddressVariable = Design.EmailAddressVariable,
                    OptOutVariable = Design.OptOutVariable,
                    SourceModule = Module!.Design.Name,
                    SourceId = Module.GetIdText(),
                    SummaryFieldName = string.IsNullOrEmpty(Design.DbColumn) ? string.Empty : Design.Name,
                };
                var result = await MailTransport.SendBulkSearchAsync(Services.Provider?.GetService<IHttpService>(), request);
                await ScriptObjects.Mail.LogFailuresAsync(Services, result);

                //サーバーが列に書いた内容と同等のものでローカル表示も更新する(正値は次回ロードのサーバー値)
                if (!string.IsNullOrEmpty(Design.DbColumn))
                {
                    var json = BulkMailSummary.Prepend(Value,
                        BulkMailSummary.CreateEntry(Design.SenderName, subject, result, DateTime.Now));
                    await InitializeDataAsync(new BulkMailFieldData { Value = json });
                    NotifyStateChanged();
                }
                return result;
            }
            finally
            {
                _isSending = false;
                NotifyStateChanged();
            }
        }

        //変数指定(自モジュールのフィールド)を優先し、無ければ固定文字列を使う
        string ResolveTemplate(string variable, string literal)
        {
            if (string.IsNullOrEmpty(variable)) return literal;
            return MailVariableResolver.GetValueText(Module!.GetData(), variable);
        }
    }
}
