using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// GanttField の依存関係 (DependenciesModule) は "フィールド名:Dependencies" の従属宣言として版に入り、復元で版の行に差し替わる。
    /// 物理削除で新しい行として作り直したタスクを指す依存関係は新しい行 (仮 Id) に付け替え、版にも今にも無いタスクを指す依存関係は落とす。
    /// </summary>
    public class EditHistoryGanttDependenciesTest
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
                DependenciesModule = new SearchCondition("TaskDependency")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Project.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                },
                DependencySourceIdField = "Source",
                DependencyDestinationIdField = "Destination",
            });
            project.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(project);

            //タスクは物理削除 (無い行は新しい行として作り直す = Id が変わる)
            var task = new ModuleDesign { Name = "ProjectTask", DataSourceName = "Main", DbTable = "project_tasks" };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            task.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "タスク名", DbColumn = "title" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Start", DbColumn = "start" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "End", DbColumn = "end" });
            task.Fields.Add(new NumberFieldDesign { Name = "Progress", DbColumn = "progress" });
            task.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(task);

            var dependency = new ModuleDesign { Name = "TaskDependency", DataSourceName = "Main", DbTable = "task_dependencies" };
            dependency.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Source", DisplayName = "元", SearchCondition = new SearchCondition("ProjectTask"), DbColumn = "source_id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Destination", DisplayName = "先", SearchCondition = new SearchCondition("ProjectTask"), DbColumn = "destination_id" });
            dependency.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(dependency);
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

        static ModuleData Dependency(string id, string source, string destination)
        {
            var data = new ModuleData { Name = "TaskDependency" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Project"] = new LinkFieldData { Value = "1" };
            data.Fields["Source"] = new LinkFieldData { Value = source };
            data.Fields["Destination"] = new LinkFieldData { Value = destination };
            return data;
        }

        [Test]
        public void 依存関係モジュールがあればタスクと依存関係の2つを宣言する()
        {
            var d = CreateDesign();
            var gantt = (GanttFieldDesign)d.Modules.Find("Project")!.Fields.First(e => e.Name == "Gantt");
            Assert.That(gantt.GetOwnedRecords(d.Modules).Select(e => (e.Name, e.Condition.ModuleName)),
                Is.EqualTo(new[] { ("Gantt", "ProjectTask"), ("Gantt:Dependencies", "TaskDependency") }));

            gantt.DependenciesModule = new SearchCondition();
            Assert.That(gantt.GetOwnedRecords(d.Modules).Select(e => e.Name), Is.EqualTo(new[] { "Gantt" }), "依存関係モジュールが無ければタスクだけ");
        }

        [Test]
        public void 差分では依存関係がフィールドの表示名に接尾辞を付けた名前で出る()
        {
            var design = CreateDesign();
            var before = new ModuleData { Name = "Project" };
            before.Fields["Id"] = new IdFieldData { Value = "1" };
            before.Fields["Gantt"] = new ListFieldData { Children = [Task("1", "要件定義", 1), Task("2", "設計", 5)] };
            before.Fields["Gantt:Dependencies"] = new ListFieldData { Children = [] };
            var after = before.JsonClone();
            after.Fields["Gantt:Dependencies"] = new ListFieldData { Children = [Dependency("d1", "1", "2")] };

            var changes = EditHistoryDiff.Compute(design, design.Modules.Find("Project")!, before, after, (_, _) => true);
            Assert.That(changes.Select(e => (e.FieldName, e.DisplayName, e.AddedCount)), Is.EqualTo(new[] { ("Gantt:Dependencies", "工程:Dependencies", 1) }));
        }

        [Test]
        public async Task 復元は依存関係も版の行に差し替え_作り直したタスクへの依存は新しい行に付け替え_無いタスクへの依存は落とす()
        {
            var design = CreateDesign();
            var services = new TestServices(design);
            //DB: タスク 1, 2 / 依存関係 d1 (1 → 2)
            services.App.ListProvider = request => request.Condition.ModuleName switch
            {
                "ProjectTask" => new Paging<ModuleData> { TotalCount = 2, Items = [Task("1", "要件定義", 1), Task("2", "余分", 5)] },
                "TaskDependency" => new Paging<ModuleData> { TotalCount = 1, Items = [Dependency("d1", "1", "2")] },
                _ => new Paging<ModuleData>(),
            };

            var projectData = new ModuleData { Name = "Project" };
            projectData.Fields["Id"] = new IdFieldData { Value = "1" };
            projectData.Fields["Name"] = new TextFieldData { Value = "P" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, projectData, ModuleLayoutType.None);
            var gantt = module.GetField<GanttField>("Gantt")!;

            //版: タスク 1, 3 (DB に無い = 物理削除なので新しい行) / 依存関係 d2 (1 → 3), d3 (1 → 9: 版にも今にも無いタスク)
            var snapshot = new ModuleData { Name = "Project" };
            snapshot.Fields["Id"] = new IdFieldData { Value = "1" };
            snapshot.Fields["Gantt"] = new ListFieldData { Children = [Task("1", "要件定義", 1), Task("3", "設計", 10)] };
            snapshot.Fields["Gantt:Dependencies"] = new ListFieldData { Children = [Dependency("d2", "1", "3"), Dependency("d3", "1", "9")] };

            var applied = await EditHistoryRestorer.ApplyAsync(module, snapshot, null);
            Assert.That(applied, Is.EqualTo(2), "タスクと依存関係の 2 宣言を差し替えた");

            var submit = gantt.GetSubmitData();
            var newTask = submit.Add.Single(e => e.Name == "ProjectTask");
            Assert.That(((TextFieldData)newTask.Fields["Title"]).Value, Is.EqualTo("設計"));
            var newTaskId = ((IdFieldData)newTask.Fields["Id"]).Value!;
            Assert.That(newTaskId, Is.Not.EqualTo("3"), "物理削除のモジュールなので Id は振り直し (仮 Id)");

            var newDependency = submit.Add.Single(e => e.Name == "TaskDependency");
            Assert.That(((LinkFieldData)newDependency.Fields["Source"]).Value, Is.EqualTo("1"));
            Assert.That(((LinkFieldData)newDependency.Fields["Destination"]).Value, Is.EqualTo(newTaskId), "作り直したタスクの仮 Id に付け替える (保存時に採番 Id に解決される)");
            Assert.That(submit.Delete.Select(e => (e.ModuleName, e.Id)), Is.EquivalentTo(new[] { ("ProjectTask", "2"), ("TaskDependency", "d1") }), "版に無い行は削除");
            Assert.That(submit.Add.Count, Is.EqualTo(2), "無いタスク 9 への依存関係は作らない");
        }
    }
}
