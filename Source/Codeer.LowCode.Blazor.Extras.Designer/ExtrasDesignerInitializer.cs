using Codeer.LowCode.Blazor.Extras.Designer.Controls;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Models;
using Codeer.LowCode.Blazor.Designer;

namespace Codeer.LowCode.Blazor.Extras.Designer
{
    public static class ExtrasDesignerInitializer
    {
        public static void Initialize()
        {
            //load dll.
            typeof(TaskBoardFieldDesign).ToString();

            //script runtime types.
            DesignerApp.ScriptRuntimeTypeManager.AddType<CalendarViewMode>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<GanttViewMode>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Marker>();

            //custom property controls.
            PropertyTypeManager.AddPropertyControl<TaskBoardStatuses, TaskBoardStatusesPropertyControl>();
        }
    }
}
