using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
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
    /// 従属レコードを宣言した拡張フィールド (GanttField) の復元: 版のタスク群で差し替え、
    /// 既存は Id で更新、無い行は論理削除なら Id を保って復活、余りは削除。
    /// </summary>
    public class EditHistoryGanttRestoreTest
    {
        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var project = new ModuleDesign { Name = "Project", DataSourceName = "Main", DbTable = "projects" };
            project.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            project.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            project.Fields.Add(new GanttFieldDesign
            {
                Name = "Gantt", DisplayName = "工程", TextField = "Title", StartField = "Start", EndField = "End", ProgressField = "Progress", IdField = "Id",
                SearchCondition = new SearchCondition("ProjectTask")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Project.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                },
            });
            project.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(project);

            var task = new ModuleDesign { Name = "ProjectTask", DataSourceName = "Main", DbTable = "project_tasks" };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            task.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "タスク名", DbColumn = "title" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Start", DbColumn = "start" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "End", DbColumn = "end" });
            task.Fields.Add(new NumberFieldDesign { Name = "Progress", DbColumn = "progress" });
            task.Fields.Add(new BooleanFieldDesign { Name = SystemFieldNames.LogicalDelete, DbColumn = "is_deleted" });
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
            data.Fields["Progress"] = new NumberFieldData { Value = 0 };
            return data;
        }

        [Test]
        public async Task 宣言した従属レコードの復元は既存をIdで更新し無い行は復活し余りは削除する()
        {
            var design = CreateDesign();
            var services = new TestServices(design);
            //DB にあるタスク: 1 (名前が変わっている), 2 (版には無い = 余り)
            services.App.ListProvider = _ => new Paging<ModuleData> { TotalCount = 2, Items = [Task("1", "要件定義(改)", 1), Task("2", "余分", 5)] };

            var projectData = new ModuleData { Name = "Project" };
            projectData.Fields["Id"] = new IdFieldData { Value = "1" };
            projectData.Fields["Name"] = new TextFieldData { Value = "P" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, projectData, ModuleLayoutType.None);
            var gantt = module.GetField<GanttField>("Gantt")!;

            //版のタスク群: 1 (元の名前), 3 (論理削除されていた行 = Id を保って復活)
            var snapshot = new ModuleData { Name = "Project" };
            snapshot.Fields["Id"] = new IdFieldData { Value = "1" };
            snapshot.Fields["Gantt"] = new ListFieldData { Children = [Task("1", "要件定義", 1), Task("3", "設計", 10)] };

            var revived = new List<(string, string)>();
            await EditHistoryRestorer.ApplyAsync(module, snapshot, (m, id) => revived.Add((m, id)));

            var submit = gantt.GetSubmitData();
            Assert.That(submit.Update.Select(e => (((IdFieldData)e.Fields["Id"]).Value, ((TextFieldData)e.Fields["Title"]).Value)),
                Is.EquivalentTo(new[] { ("1", "要件定義"), ("3", "設計") }), "既存 1 は更新、復活した 3 も既存行として Update");
            Assert.That(submit.Delete.Select(e => e.Id), Is.EqualTo(new[] { "2" }), "版に無い行は削除");
            Assert.That(submit.Add, Is.Empty);
            Assert.That(revived, Is.EqualTo(new[] { ("ProjectTask", "3") }), "論理削除のモジュールなので Id を保って復活 = Undelete を登録");
        }
    }
}
