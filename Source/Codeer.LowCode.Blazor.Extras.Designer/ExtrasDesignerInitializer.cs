using Codeer.LowCode.Blazor.Extras.Designer.Controls;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using ScriptObjects = Codeer.LowCode.Blazor.Extras.ScriptObjects;

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

            //Extras のスクリプトオブジェクト/サービス (スクリプト補完・script-catalog 用)。
            //デザイナでは実行されないため依存はダミーでよい。差し替えたいアプリは
            //この後で同名の AddService を呼べば上書きできる (type.Name キーで後勝ち)。
            DesignerApp.ScriptRuntimeTypeManager.AddType(typeof(ScriptObjects.Excel));
            DesignerApp.ScriptRuntimeTypeManager.AddType<ScriptObjects.ExcelCellIndex>();
            DesignerApp.ScriptRuntimeTypeManager.AddService(new ScriptObjects.Toaster(null!));
            DesignerApp.ScriptRuntimeTypeManager.AddService(new ScriptObjects.WebApiService(null!, null!));
            DesignerApp.ScriptRuntimeTypeManager.AddType<ScriptObjects.WebApiResult>();
            DesignerApp.ScriptRuntimeTypeManager.AddService(new ScriptObjects.MailService());
            DesignerApp.ScriptRuntimeTypeManager.AddType<ScriptObjects.MailMessage>();
            DesignerApp.ScriptRuntimeTypeManager.AddService(new ScriptObjects.BulkFileTransferService());

            //custom property controls.
            PropertyTypeManager.AddPropertyControl<TaskBoardStatuses, TaskBoardStatusesPropertyControl>();
            PropertyTypeManager.AddPropertyControl<MappingColumns, MappingColumnsPropertyControl>();

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
