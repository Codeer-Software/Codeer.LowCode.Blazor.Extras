using Codeer.LowCode.Blazor.DataIO;
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
    /// 単発メール送信フィールドのランタイム。デザインで宣言した宛先・件名・本文テンプレートを
    /// 自レコードの値で解決して送信する (UI・データは持たない)。
    /// </summary>
    public class MailField(MailFieldDesign design) : FieldBase<MailFieldDesign>(design)
    {
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

        /// <summary>
        /// 送信する。宛先・件名・本文はデザインの宣言どおり自レコードの値で解決する
        /// (テンプレートの {変数} はリンクパス可。クライアント評価に使うリンク先の値は
        /// DataOnlyFields 等でロードされていること)。
        /// </summary>
        [ScriptName("Send")]
        public async Task<MailSendResult> SendAsync()
        {
            if (Services.AppInfoService.IsDesignMode) return new();
            if (Module == null) return MailSendResult.Failure(string.Empty, "The field is not ready.");

            var data = Module.GetData();
            var design = Services.AppInfoService.GetDesignData().Modules.Find(Module.Design.Name);

            var to = SplitAddresses(ResolveAddress(data, Design.ToVariable, Design.To));
            if (to.Count == 0) return MailSendResult.Failure(string.Empty, Properties.Resources.MailFieldNoRecipient);

            //テンプレート (変数指定があればそのフィールド値、無ければ固定文字列) を自レコードで差し込み解決
            var subjectTemplate = ResolveTemplate(data, Design.SubjectVariable, Design.Subject);
            var bodyTemplate = ResolveTemplate(data, Design.BodyVariable, Design.Body);
            var names = MailTemplateEngine.GetVariableNames(subjectTemplate)
                .Concat(MailTemplateEngine.GetVariableNames(bodyTemplate)).Distinct().ToList();
            var variables = MailVariableResolver.Resolve(design, data, names,
                name => Services.AppInfoService.GetDesignData().Modules.Find(name));

            var request = new MailSendRequest
            {
                SenderName = Design.SenderName,
                SourceModule = Module.Design.Name,
                SourceId = MailVariableResolver.GetValueText(data, Codeer.LowCode.Blazor.DesignLogic.SystemFieldNames.Id),
                Message = new MailMessage
                {
                    To = to,
                    Cc = SplitAddresses(ResolveAddress(data, Design.CcVariable, Design.Cc)),
                    Subject = MailTemplateEngine.Fill(subjectTemplate, variables),
                    Body = MailTemplateEngine.Fill(bodyTemplate, variables),
                    IsBodyHtml = Design.IsBodyHtml,
                    ReplyTo = Design.ReplyTo,
                },
            };
            return await MailTransport.SendAsync(GetHttpService(), request);
        }

        static string ResolveAddress(ModuleData data, string variable, string literal)
            => string.IsNullOrEmpty(variable) ? literal : MailVariableResolver.GetValueText(data, variable);

        static string ResolveTemplate(ModuleData data, string variable, string literal)
            => string.IsNullOrEmpty(variable) ? literal : MailVariableResolver.GetValueText(data, variable);

        static List<string> SplitAddresses(string addresses)
            => addresses.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        IHttpService? GetHttpService() => Services.Provider?.GetService<IHttpService>();
    }
}
