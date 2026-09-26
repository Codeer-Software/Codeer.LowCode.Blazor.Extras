using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// 自分の Id で子レコードを検索するフィールド (Gantt / Calendar / TaskBoard / MarkerList) は、
    /// 未保存 (新規) レコード上では読みに行かない (仮 Id を DB に渡すと変換エラーになる)。保存済みなら読む。
    /// </summary>
    public class UnsavedRecordGuardTest
    {
        static SearchCondition BindToOwnId() => new("ProjectTask")
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

        static IEnumerable<FieldDesignBase> Owners()
        {
            yield return new GanttFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id", SearchCondition = BindToOwnId() };
            yield return new CalendarFieldDesign { Name = "Owner", TextField = "Title", StartField = "Start", EndField = "End", SearchCondition = BindToOwnId() };
            yield return new TaskBoardFieldDesign { Name = "Owner", StatusField = "Status", SortIndexField = "SortIndex", SearchCondition = BindToOwnId() };
            yield return new MarkerListFieldDesign { Name = "Owner", XField = "X", YField = "Y", LabelField = "Title", SearchCondition = BindToOwnId() };
        }

        static async Task<int> CountLoadsAsync(FieldDesignBase owner, string? id)
        {
            var services = new TestServices(CreateDesign(owner));
            var loads = 0;
            services.App.ListProvider = _ => { loads++; return new Paging<ModuleData>(); };
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None);
            var field = module.GetField("Owner")!;
            switch (field)
            {
                case GanttField g: await g.ReloadAsync(); break;
                case CalendarField c: await c.ReloadAsync(); break;
                case TaskBoardField t: await t.ReloadAsync(); break;
                case MarkerListField m: await m.ReloadAsync(); break;
                default: Assert.Fail(field.GetType().Name); break;
            }
            return loads;
        }

        [Test]
        public async Task 未保存レコード上では子レコードを読みに行かない()
        {
            foreach (var owner in Owners())
                Assert.That(await CountLoadsAsync(owner, null), Is.EqualTo(0), owner.GetType().Name);
        }

        [Test]
        public async Task 保存済みレコード上では子レコードを読む()
        {
            foreach (var owner in Owners())
                Assert.That(await CountLoadsAsync(owner, "1"), Is.GreaterThan(0), owner.GetType().Name);
        }
    }
}
