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
            DesignerApp.ScriptRuntimeTypeManager.AddType<ScriptObjects.Mail>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Codeer.LowCode.Blazor.Extras.Mail.MailSendResult>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Codeer.LowCode.Blazor.Extras.Mail.MailSendFailure>();
            DesignerApp.ScriptRuntimeTypeManager.AddService(new ScriptObjects.BulkFileTransferService());
            //new BulkFileReader<XXXModule>() のイディオム (モジュール名が ctor に渡る) で使える
            DesignerApp.ScriptRuntimeTypeManager.AddModuleGenericType(typeof(ScriptObjects.BulkFileReader));
            //承認フロー (経路のスクリプト組み立てと command API の応答)
            DesignerApp.ScriptRuntimeTypeManager.AddType<Extras.Approval.ApprovalRouteData>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Extras.Approval.ApprovalStepData>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Extras.Approval.ApprovalMemberData>();
            DesignerApp.ScriptRuntimeTypeManager.AddType<Extras.Approval.ApprovalActionResult>();

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
