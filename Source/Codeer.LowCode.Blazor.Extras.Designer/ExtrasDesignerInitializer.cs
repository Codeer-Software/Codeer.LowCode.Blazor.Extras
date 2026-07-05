using Codeer.LowCode.Blazor.Extras.Designer.Controls;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Extensibility;

namespace Codeer.LowCode.Blazor.Extras.Designer
{
    public static class ExtrasDesignerInitializer
    {
        public static void Initialize(BlazorRuntime blazorRuntime)
        {
            InitializeCore();
            blazorRuntime.InstallBundleCss("Codeer.LowCode.Blazor.Extras");
        }

        [Obsolete("Use Initialize(BlazorRuntime) instead. Without it, scoped CSS for Extras components is not installed.")]
        public static void Initialize() => InitializeCore();

        static void InitializeCore()
        {
            //load dll.
            typeof(TaskBoardFieldDesign).ToString();

            //script runtime types.
            DesignerApp.ScriptRuntimeTypeManager.AddType<CalendarViewMode>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<GanttViewMode>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Marker>();

            //custom property controls.
            PropertyTypeManager.AddPropertyControl<TaskBoardStatuses, TaskBoardStatusesPropertyControl>();

            //AI 用フィールドドキュメントを登録(ライブラリ本体は Designer を参照しないため、ここで吸収する)。
            foreach (var kv in ExtrasFieldDocs.GetFieldDocs())
                FieldCatalog.Add(kv.Key, kv.Value);
        }
    }
}
