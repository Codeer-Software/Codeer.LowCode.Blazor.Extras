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
    /// 従属レコードを宣言した拡張フィールド (Calendar / TaskBoard) の復元。Gantt と同じ規則で
    /// 既存は Id で更新、無い行は論理削除なら Id を保って復活、余りは削除。
    /// </summary>
    public class EditHistoryOwnedFieldsRestoreTest
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
            data.Fields["Status"] = new TextFieldData { Value = "Todo" };
            data.Fields["SortIndex"] = new NumberFieldData { Value = day };
            return data;
        }

        static async Task RunAsync(FieldDesignBase ownerField, Func<Module, FieldBase> getField)
        {
            var design = CreateDesign(ownerField);
            var services = new TestServices(design);
            services.App.ListProvider = _ => new Paging<ModuleData> { TotalCount = 2, Items = [Task("1", "要件定義(改)", 1), Task("2", "余分", 5)] };
            var projectData = new ModuleData { Name = "Project" };
            projectData.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, projectData, ModuleLayoutType.None);
            var field = getField(module);
            Assert.That(field, Is.InstanceOf<IOwnedRecordsField>());

            var snapshot = new ModuleData { Name = "Project" };
            snapshot.Fields["Id"] = new IdFieldData { Value = "1" };
            snapshot.Fields[ownerField.Name] = new ListFieldData { Children = [Task("1", "要件定義", 1), Task("3", "設計", 10)] };

            var revived = new List<(string, string)>();
            await EditHistoryRestorer.ApplyAsync(module, snapshot, (m, id) => revived.Add((m, id)));

            var submit = field.GetSubmitData();
            Assert.That(submit.Update.Select(e => (((IdFieldData)e.Fields["Id"]).Value, ((TextFieldData)e.Fields["Title"]).Value)),
                Is.EquivalentTo(new[] { ("1", "要件定義"), ("3", "設計") }));
            Assert.That(submit.Delete.Select(e => e.Id), Is.EqualTo(new[] { "2" }));
            Assert.That(submit.Add, Is.Empty);
            Assert.That(revived, Is.EqualTo(new[] { ("ProjectTask", "3") }));
        }

        [Test]
        public async Task カレンダーの従属レコードを復元できる()
            => await RunAsync(
                new CalendarFieldDesign { Name = "Calendar", TextField = "Title", StartField = "Start", EndField = "End", SearchCondition = Bind() },
                m => m.GetField<CalendarField>("Calendar")!);

        [Test]
        public async Task カンバンの従属レコードを復元できる()
            => await RunAsync(
                new TaskBoardFieldDesign { Name = "Board", StatusField = "Status", SortIndexField = "SortIndex", SearchCondition = Bind() },
                m => m.GetField<TaskBoardField>("Board")!);
    }
}
