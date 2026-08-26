using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 一斉メール送信ボタン。宛先は同一モジュール上のリストフィールドの検索条件から
    /// サーバー側で解決される (全行対象・アドレスはクライアントに渡らない)。
    /// 未保存の変更がある間は送信できない (保存済みの状態=サーバーから見える状態が送信対象)。
    /// </summary>
    public class BulkMailField(BulkMailFieldDesign design) : FieldBase<BulkMailFieldDesign>(design)
    {
        bool _isSending;

        /// <summary>送信中か (ボタンの二重実行防止)。</summary>
        internal bool IsSending => _isSending;

        //ボタン (値もデータも持たない)
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

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
            if (listField?.GetSearchCondition() == null)
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
            if (listField?.GetSearchCondition() == null)
                return MailSendResult.Failure(string.Empty, "The recipient list is not configured.");

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

                var subject = ResolveValueFirst(Design.Subject, Design.SubjectVariable);
                var body = ResolveValueFirst(Design.Body, Design.BodyVariable);

                var request = new MailBulkSearchRequest
                {
                    MailInfraName = Design.MailInfraName,
                    IsFromCurrentUser = Design.IsFromCurrentUser,
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = Design.IsBodyHtml,
                    ReplyTo = ResolveValueFirst(Design.ReplyTo, Design.ReplyToVariable),
                    Condition = condition,
                    SourceModule = Module!.Design.Name,
                    SourceId = Module.GetIdText(),
                };
                var result = await MailTransport.SendBulkSearchAsync(Services.Provider?.GetService<IHttpService>(), request);
                await MailSendLogger.LogFailuresAsync(Services, result);
                return result;
            }
            finally
            {
                _isSending = false;
                NotifyStateChanged();
            }
        }

        //値優先: 値が入っていればそれを使い、空なら変数(自モジュールのフィールド)を解決する
        string ResolveValueFirst(string value, string variable)
            => !string.IsNullOrEmpty(value) ? value
                : string.IsNullOrEmpty(variable) ? string.Empty
                : MailVariableResolver.GetValueText(Module!.GetData(), variable);
    }
}
