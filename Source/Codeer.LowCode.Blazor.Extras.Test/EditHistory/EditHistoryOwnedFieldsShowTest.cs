using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 版表示: 従属レコードを宣言した拡張フィールド (Gantt / Calendar / TaskBoard / MarkerList) は
    /// DB を読まずに版の行 (OwnedRecordRow) をそのまま表示専用で見せ、行のクラスを項目の行モジュールに写す。
    /// </summary>
    public class EditHistoryOwnedFieldsShowTest
    {
        static SearchCondition Bind() => new("ProjectTask")
        {
            Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Project.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
        };

        static DesignData CreateDesign(FieldDesignBase ownerField)
        {
            var d = new DesignData();
            var project = new ModuleDesign { Name = "Project", DataSourceName = "Main", DbTable = "projects" };
            project.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            project.Fields.Add(ownerField);
            project.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(project);

            var task = new ModuleDesign { Name = "ProjectTask", DataSourceName = "Main", DbTable = "project_tasks" };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            task.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Start", DbColumn = "start" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "End", DbColumn = "end" });
            task.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "status" });
            task.Fields.Add(new NumberFieldDesign { Name = "SortIndex", DbColumn = "sort_index" });
            task.Fields.Add(new NumberFieldDesign { Name = "X", DbColumn = "x" });
            task.Fields.Add(new NumberFieldDesign { Name = "Y", DbColumn = "y" });
            task.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(task);
            return d;
        }

        static ModuleData Task(string id, string title, int day)
        {
            var data = new ModuleData { Name = "ProjectTask" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Project"] = new LinkFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Start"] = new DateTimeFieldData { Value = new DateTime(2026, 10, day) };
            data.Fields["End"] = new DateTimeFieldData { Value = new DateTime(2026, 10, day + 1) };
            data.Fields["Status"] = new TextFieldData { Value = "Todo" };
            data.Fields["SortIndex"] = new NumberFieldData { Value = day };
            data.Fields["X"] = new NumberFieldData { Value = day };
            data.Fields["Y"] = new NumberFieldData { Value = day };
            return data;
        }

        static OwnedRecordRow Row(ModuleData data, string className = "") => new() { Data = data, ClassName = className };

        static List<Module?> ItemModules(IOwnedRecordsField field) => field switch
        {
            GanttField g => g.Items.Select(e => e.Module).ToList(),
            CalendarField c => c.Items.Select(e => e.Module).ToList(),
            TaskBoardField t => t.Items.Select(e => e.Module).ToList(),
            MarkerListField m => m.MarkerList.Select(e => e.Module).ToList(),
            _ => new List<Module?>(),
        };

        static async Task<(int Loads, int Shown, int Decorated)> ShowAsync(FieldDesignBase owner)
        {
            var services = new TestServices(CreateDesign(owner));
            var loads = 0;
            services.App.ListProvider = _ => { loads++; return new Paging<ModuleData>(); };
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None);
            var field = (IOwnedRecordsField)module.GetField("Owner")!;
            await field.ShowOwnedRecordsAsync("Owner", [Row(Task("1", "要件定義", 1), "deco"), Row(Task("3", "設計", 3))]);
            //項目の描画は表示専用の行に付けたクラスを出す (版表示の強調はここに付く)
            var modules = ItemModules(field);
            return (loads, modules.Count, modules.Count(e => e.ShownClassName() == "deco"));
        }

        [Test]
        public async Task ガントチャートは版のタスクをDBを読まずに表示し表示範囲も合わせる()
        {
            var r = await ShowAsync(new GanttFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2, 1)));
        }

        [Test]
        public async Task ガントチャートは装飾された行が表示範囲に無ければその行の日へ移動する()
        {
            var services = new TestServices(CreateDesign(new GanttFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id", SearchCondition = Bind() }));
            services.App.ListProvider = _ => new Paging<ModuleData>();
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None);
            var gantt = module.GetField<GanttField>("Owner")!;
            //表示範囲は今週 (装飾なしの行 1 = 今日 が見えている)。装飾された行は 2 年後
            var near = Task("1", "要件定義", 1);
            near.Fields["Start"] = new DateTimeFieldData { Value = DateTime.Today };
            near.Fields["End"] = new DateTimeFieldData { Value = DateTime.Today.AddDays(1) };
            var farStart = DateTime.Today.AddYears(2);
            var far = Task("9", "遠いタスク", 1);
            far.Fields["Start"] = new DateTimeFieldData { Value = farStart };
            far.Fields["End"] = new DateTimeFieldData { Value = farStart.AddDays(5) };
            await gantt.ShowOwnedRecordsAsync("Owner", [Row(near), Row(far, "deco")]);
            Assert.That(gantt.ViewStart, Is.EqualTo(farStart.Date));
            Assert.That(gantt.Items.Any(e => e.Module.ShownClassName() == "deco"), Is.True);
        }

        //行のモジュールのレイアウトにデザインで付けたクラス (DetailLayoutDesign.ClassName) は行モジュールの ClassName に入るが、
        //項目 (バー・予定・カード・マーカー) に出すのは版表示の強調のクラスだけ (通常の表示の項目にレイアウトのクラスを出さない)
        [Test]
        public async Task 項目に出すクラスは強調だけでレイアウトのクラスは出さない()
        {
            var design = CreateDesign(new GanttFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id", SearchCondition = Bind() });
            design.Modules.Find("ProjectTask")!.DetailLayouts[""] = new DetailLayoutDesign { ClassName = "task-layout" };
            var services = new TestServices(design);

            //通常の読み込み: レイアウトのクラスは行モジュールにあるが、項目には何も出さない
            services.App.ListProvider = _ => new Paging<ModuleData> { Items = [Task("1", "要件定義", 1)], TotalCount = 1 };
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None);
            var gantt = module.GetField<GanttField>("Owner")!;
            await gantt.SetViewStartAsync(new DateTime(2026, 10, 1));
            var loaded = gantt.Items.Single().Module!;
            Assert.That(loaded.ClassName, Is.EqualTo("task-layout"));
            Assert.That(loaded.ShownClassName(), Is.Empty);

            //版表示: 強調のクラスだけを出す
            await gantt.ShowOwnedRecordsAsync("Owner", [Row(Task("1", "要件定義", 1), "deco"), Row(Task("3", "設計", 3))]);
            Assert.That(gantt.Items.Select(e => e.Module.ShownClassName()), Is.EqualTo(new[] { "deco", "" }));
        }

        [Test]
        public async Task カレンダーは版の予定をDBを読まずに表示する()
        {
            var r = await ShowAsync(new CalendarFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2, 1)));
        }

        [Test]
        public async Task カンバンは版のカードをDBを読まずに表示する()
        {
            var r = await ShowAsync(new TaskBoardFieldDesign { Name = "Owner", StatusField = "Status", SortIndexField = "SortIndex", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2, 1)));
        }

        [Test]
        public async Task マーカーリストは版のマーカーをDBを読まずに表示する()
        {
            var r = await ShowAsync(new MarkerListFieldDesign { Name = "Owner", XField = "X", YField = "Y", LabelField = "Title", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2, 1)));
        }
    }
}
