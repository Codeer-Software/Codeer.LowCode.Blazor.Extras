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
        /// Endpoint URLs belong to the app, so pass them via <paramref name="options"/>.
        /// </summary>
        public static void Initialize(IAppInfoService app, IHttpService http, ILogger logger, IToasterEx toaster, ExtrasClientOptions options)
        {
            Initialize(app);

            var manager = app.GetScriptRuntimeTypeManager();
            manager.AddCustomInjector(() => http);
            manager.AddCustomInjector(() => options);
            manager.AddType(typeof(ScriptObjects.Excel));
            manager.AddType(typeof(ExcelCellIndex));
            manager.AddType<WebApiResult>();
            manager.AddType<MailMessage>();
            manager.AddService(new WebApiService(http, logger));
            manager.AddService(new Toaster(toaster));
            manager.AddService(new MailService());
        }
    }
}
