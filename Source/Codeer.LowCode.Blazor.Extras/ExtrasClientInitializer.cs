using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.RequestInterfaces;

namespace Codeer.LowCode.Blazor.Extras
{
    public static class ExtrasClientInitializer
    {
        public static void Initialize(IAppInfoService app)
        {
            //load dll.
            typeof(TaskBoardFieldDesign).ToString();

            //script runtime types.
            app.GetScriptRuntimeTypeManager().AddType<CalendarViewMode>();
            app.GetScriptRuntimeTypeManager().AddType<GanttViewMode>();
        }

        /// <summary>
        /// Initialize including built-in script objects (Excel, WebApi, Toaster, Mail).
        /// Endpoint URLs belong to the app, so set them once at startup via the static
        /// properties of each feature (MailTransport.SendMailEndPoint /
        /// BulkSearchMailEndPoint, Excel.ConvertPdfEndPoint,
        /// AITextAnalyzerField.FileToModuleDataEndPoint / TextToModuleDataEndPoint).
        /// </summary>
        public static void Initialize(IAppInfoService app, IHttpService http, ILogger logger, IToastService toaster)
        {
            Initialize(app);

            var manager = app.GetScriptRuntimeTypeManager();
            manager.AddCustomInjector(() => http);
            manager.AddType(typeof(ScriptObjects.Excel));
            manager.AddType(typeof(ExcelCellIndex));
            manager.AddModuleGenericType(typeof(ScriptObjects.BulkFileReader));
            manager.AddType<WebApiResult>();
            manager.AddType<ScriptObjects.Mail>();
            manager.AddType<Codeer.LowCode.Blazor.Extras.Mail.MailSendResult>();
            manager.AddType<Codeer.LowCode.Blazor.Extras.Mail.MailSendFailure>();
            //承認フロー (経路のスクリプト組み立てと command API の応答)
            manager.AddType<Approval.ApprovalRouteData>();
            manager.AddType<Approval.ApprovalStepData>();
            manager.AddType<Approval.ApprovalMemberData>();
            manager.AddType<Approval.ApprovalActionResult>();
            manager.AddService(new WebApiService(http, logger));
            manager.AddService(new Toaster(toaster));
            manager.AddService(new BulkFileTransferService());
        }
    }
}
