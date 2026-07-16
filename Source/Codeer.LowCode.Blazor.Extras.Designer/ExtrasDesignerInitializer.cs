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

            //AI 用フィールドドキュメント。.md は Extras 本体プロジェクトにあるが、WASM に配信しないため
            //このアセンブリに埋め込まれている (リンク EmbeddedResource)。FieldCatalog が
            //`.FieldDocs.<型名>.md` 規約で解決できるよう、探索先として登録する。
            FieldCatalog.AddDocAssembly(typeof(ExtrasDesignerInitializer).Assembly);

            //AI 用スクリプトオブジェクトドキュメント(Excel / WebApi / Toaster / Mail 等)も同様に
            //このアセンブリの埋め込みから登録する。
            foreach (var kv in ExtrasScriptObjectDocs.GetScriptObjectDocs())
                ScriptObjectCatalog.Add(kv.Key, kv.Value);
        }
    }
}
