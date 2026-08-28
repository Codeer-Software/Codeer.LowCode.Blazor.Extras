using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 単発メール送信フィールドのランタイム。レイアウトに置くと送信ボタンとして表示される。
    /// 各項目は「値」(このクラスのプロパティ。初期値はデザインの値・スクリプトから上書き可) と
    /// デザインの「変数」のペアで、**値が入っていれば値、空なら変数を自レコードで解決**して送信する。
    /// 件名・本文はどちらの経路でもテンプレートとして {変数} (リンクパス可) が解決される
    /// (クライアント評価に使うリンク先の値は DataOnlyFields 等でロードされていること)。
    /// スクリプトで全項目を設定すれば完全に動的な送信もできる (旧 Mail スクリプトオブジェクトの置き換え)。
    /// </summary>
    public class MailField(MailFieldDesign design) : FieldBase<MailFieldDesign>(design)
    {
        readonly List<MailAttachment> _attachments = new();

        /// <summary>宛先アドレス (カンマ / セミコロン区切りで複数可)。入っていれば ToVariable より優先。</summary>
        public string To { get; set; } = design.To;

        /// <summary>Cc アドレス。入っていれば CcVariable より優先。</summary>
        public string Cc { get; set; } = design.Cc;

        /// <summary>Bcc アドレス。入っていれば BccVariable より優先。</summary>
        public string Bcc { get; set; } = design.Bcc;

        /// <summary>件名テンプレート ({変数} は自レコードで解決)。入っていれば SubjectVariable より優先。</summary>
        public string Subject { get; set; } = design.Subject;

        /// <summary>本文テンプレート ({変数} は自レコードで解決)。入っていれば BodyVariable より優先。</summary>
        public string Body { get; set; } = design.Body;

        /// <summary>本文を HTML として送るか。</summary>
        public bool IsBodyHtml { get; set; } = design.IsBodyHtml;

        /// <summary>返信先アドレス。入っていれば ReplyToVariable より優先。</summary>
        public string ReplyTo { get; set; } = design.ReplyTo;

        bool _isSending;

        /// <summary>送信中か (ボタンの二重実行防止)。</summary>
        internal bool IsSending => _isSending;

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        /// <summary>Excel を添付する (次の Send で送られ、送信後にクリアされる)。</summary>
        [ScriptName("AddAttachment")]
        public MailField AddAttachment(string fileName, ScriptObjects.Excel excel)
        {
            _attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(excel.GetBytes()) });
            return this;
        }

        /// <summary>テキストファイルを添付する (次の Send で送られ、送信後にクリアされる)。</summary>
        [ScriptName("AddTextAttachment")]
        public MailField AddTextAttachment(string fileName, string text)
        {
            _attachments.Add(new MailAttachment { FileName = fileName, ContentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)) });
            return this;
        }

        /// <summary>ボタンからの送信。結果をトーストで知らせる。</summary>
        internal async Task SendFromButtonAsync()
        {
            if (Services.AppInfoService.IsDesignMode) return;
            if (_isSending || Module == null) return;

            //ボタンはアイコンだけなので、押した瞬間に送らず確認を挟む (スクリプトの Send() は確認なし)
            var (request, buildError) = BuildRequest();
            if (buildError != null)
            {
                await Services.UIService.NotifyError(string.Format(Properties.Resources.MailFieldSendFailedFormat, buildError));
                return;
            }
            var recipientCount = request.Message.To.Count + request.Message.Cc.Count + request.Message.Bcc.Count;
            var message = string.Format(Properties.Resources.MailFieldConfirmSendFormat, request.Message.Subject, recipientCount);
            var answer = await Services.UIService.ShowMessageBox(string.Empty, message,
                [new DialogButton("btn btn-outline-primary", Properties.Resources.BulkMailSendAction),
                 new DialogButton("btn btn-outline-secondary", Properties.Resources.Cancel)]);
            if (answer != Properties.Resources.BulkMailSendAction) return;

            var result = await SendAsync();
            if (result.IsSuccess)
            {
                await Services.UIService.NotifySuccess(Properties.Resources.MailFieldSentToast);
            }
            else
            {
                var error = result.Failures.Count == 0 ? string.Empty : result.Failures[0].Error;
                await Services.UIService.NotifyError(string.Format(Properties.Resources.MailFieldSendFailedFormat, error));
            }
        }

        /// <summary>
        /// 送信する。値 (プロパティ) が入っていれば値、空ならデザインの変数を自レコードで解決する。
        /// 送信履歴の Source はこのレコード。失敗は戻り値に加えて Logger にも記録される。
        /// </summary>
        [ScriptName("Send")]
        public async Task<MailSendResult> SendAsync()
        {
            if (Services.AppInfoService.IsDesignMode) return new();
            if (Module == null) return MailSendResult.Failure(string.Empty, "The field is not ready.");
            if (_isSending) return MailSendResult.Failure(string.Empty, "Sending is already in progress.");
            _isSending = true;
            NotifyStateChanged();
            try
            {
                return await SendCoreAsync();
            }
            finally
            {
                _isSending = false;
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// 送信せずに「送るとこうなる」を HTML でダウンロードする (差出人・宛先・解決後の件名/本文・変数のハイライト)。
        /// 文面の解決は送信と同じ経路。サーバー側のプレビュー対応 (MailTransport.PreviewMailEndPoint) が必要。
        /// </summary>
        [ScriptName("Preview")]
        public async Task<bool> PreviewAsync()
        {
            if (Services.AppInfoService.IsDesignMode || Module == null) return false;
            var (request, _) = BuildRequest();
            var preview = new MailPreviewRequest
            {
                MailInfraName = request.MailInfraName,
                Message = request.Message,
                SubjectTemplate = _lastSubjectTemplate,
                BodyTemplate = _lastBodyTemplate,
                SubjectSpans = _lastSubjectSpans,
                BodySpans = _lastBodySpans,
                Title = $"{Module.Design.Name} #{request.SourceId}",
            };
            var html = await MailTransport.PreviewAsync(Services.Provider?.GetService<IHttpService>(), preview);
            if (html == null)
            {
                await Services.UIService.NotifyError(Properties.Resources.MailPreviewFailed);
                return false;
            }
            await Services.UIService.DownloadFile(new MemoryStream(html), $"mail-preview-{Design.Name}.html");
            return true;
        }

        string _lastSubjectTemplate = string.Empty, _lastBodyTemplate = string.Empty;
        List<MailTemplateSpan> _lastSubjectSpans = new(), _lastBodySpans = new();

        async Task<MailSendResult> SendCoreAsync()
        {
            var (request, error) = BuildRequest();
            if (error != null) return MailSendResult.Failure(string.Empty, error);
            _attachments.Clear();

            var result = await MailTransport.SendAsync(Services.Provider?.GetService<IHttpService>(), request);
            await MailSendLogger.LogFailuresAsync(Services, result);
            return result;
        }

        //送信要求を組み立てる (送信とプレビューで共有 = 同じ解決結果になる)
        (MailSendRequest Request, string? Error) BuildRequest()
        {
            var data = Module!.GetData();
            var design = Services.AppInfoService.GetDesignData().Modules.Find(Module.Design.Name);

            var to = SplitAddresses(ResolveValueFirst(data, To, Design.ToVariable));

            //テンプレート (値が入っていれば値、空なら変数のフィールド値) を自レコードで差し込み解決
            var subjectTemplate = ResolveValueFirst(data, Subject, Design.SubjectVariable);
            var bodyTemplate = ResolveValueFirst(data, Body, Design.BodyVariable);
            var names = MailTemplateEngine.GetVariableNames(subjectTemplate, bodyTemplate);
            var variables = MailVariableResolver.Resolve(design, data, names,
                name => Services.AppInfoService.GetDesignData().Modules.Find(name));
            var (subject, subjectSpans) = MailTemplateEngine.FillWithSpans(subjectTemplate, variables);
            var (body, bodySpans) = MailTemplateEngine.FillWithSpans(bodyTemplate, variables);
            _lastSubjectTemplate = subjectTemplate;
            _lastBodyTemplate = bodyTemplate;
            _lastSubjectSpans = subjectSpans;
            _lastBodySpans = bodySpans;

            var request = new MailSendRequest
            {
                MailInfraName = Design.MailInfraName,
                SourceModule = Module.Design.Name,
                SourceId = MailVariableResolver.GetValueText(data, Codeer.LowCode.Blazor.DesignLogic.SystemFieldNames.Id),
                Message = new MailMessage
                {
                    To = to,
                    Cc = SplitAddresses(ResolveValueFirst(data, Cc, Design.CcVariable)),
                    Bcc = SplitAddresses(ResolveValueFirst(data, Bcc, Design.BccVariable)),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = IsBodyHtml,
                    ReplyTo = ResolveValueFirst(data, ReplyTo, Design.ReplyToVariable),
                    Attachments = _attachments.ToList(),
                },
            };
            return (request, to.Count == 0 ? Properties.Resources.MailFieldNoRecipient : null);
        }

        //値優先: 値 (プロパティ) が入っていればそれを使い、空なら変数を自レコードで解決する
        static string ResolveValueFirst(ModuleData data, string value, string variable)
            => !string.IsNullOrEmpty(value) ? value
                : string.IsNullOrEmpty(variable) ? string.Empty
                : MailVariableResolver.GetValueText(data, variable);

        static List<string> SplitAddresses(string addresses)
            => addresses.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
