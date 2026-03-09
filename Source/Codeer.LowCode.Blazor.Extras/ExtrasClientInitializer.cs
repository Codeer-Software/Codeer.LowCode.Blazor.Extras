using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
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
    }
}
