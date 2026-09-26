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
    /// DB を読まずに版の行をそのまま表示専用で見せる。
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

        static async Task<(int Loads, int Shown)> ShowAsync(FieldDesignBase owner)
        {
            var services = new TestServices(CreateDesign(owner));
            var loads = 0;
            services.App.ListProvider = _ => { loads++; return new Paging<ModuleData>(); };
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None);
            var field = (IOwnedRecordsField)module.GetField("Owner")!;
            await field.ShowOwnedRecordsAsync("Owner", [Task("1", "要件定義", 1), Task("3", "設計", 3)]);
            var shown = field switch
            {
                GanttField g => g.Items.Count,
                CalendarField c => c.Items.Count,
                TaskBoardField t => t.Items.Count,
                MarkerListField m => m.MarkerList.Count,
                _ => -1,
            };
            return (loads, shown);
        }

        [Test]
        public async Task ガントチャートは版のタスクをDBを読まずに表示し表示範囲も合わせる()
        {
            var r = await ShowAsync(new GanttFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2)));
        }

        [Test]
        public async Task カレンダーは版の予定をDBを読まずに表示する()
        {
            var r = await ShowAsync(new CalendarFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2)));
        }

        [Test]
        public async Task カンバンは版のカードをDBを読まずに表示する()
        {
            var r = await ShowAsync(new TaskBoardFieldDesign { Name = "Owner", StatusField = "Status", SortIndexField = "SortIndex", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2)));
        }

        [Test]
        public async Task マーカーリストは版のマーカーをDBを読まずに表示する()
        {
            var r = await ShowAsync(new MarkerListFieldDesign { Name = "Owner", XField = "X", YField = "Y", LabelField = "Title", SearchCondition = Bind() });
            Assert.That(r, Is.EqualTo((0, 2)));
        }
    }
}
