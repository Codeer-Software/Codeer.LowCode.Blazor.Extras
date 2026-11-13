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
    /// ガントの表示開始日: ガントは基準日を 1 つ持ち、基準日を含む日 / 週 / 月を表示する。
    /// 表示モードの切り替えは基準日を変えない (CalendarField と同じ)。
    /// </summary>
    public class GanttViewStartTest
    {
        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var project = new ModuleDesign { Name = "Project", DataSourceName = "Main", DbTable = "projects" };
            project.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            project.Fields.Add(new GanttFieldDesign
            {
                Name = "Gantt", TextField = "Title", StartField = "Start", EndField = "End", IdField = "Id",
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
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "Start", DbColumn = "start" });
            task.Fields.Add(new DateTimeFieldDesign { Name = "End", DbColumn = "end" });
            task.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(task);
            return d;
        }

        static async Task<GanttField> CreateGanttAsync()
        {
            var services = new TestServices(CreateDesign());
            services.App.ListProvider = _ => new Paging<ModuleData>();
            var data = new ModuleData { Name = "Project" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None);
            return module.GetField<GanttField>("Gantt")!;
        }

        static DateTime SundayOf(DateTime d) => d.Date.AddDays(-(int)d.DayOfWeek);

        //基準日を 2026/11/12 (木) にした週表示 (11/8〜) から始める
        static async Task<GanttField> CreateAt1112Async()
        {
            var gantt = await CreateGanttAsync();
            await gantt.GoTodayAsync();
            await gantt.SetViewStartAsync(new DateTime(2026, 11, 12));
            return gantt;
        }

        [Test]
        public async Task 初期表示から日へ切り替えると今日を開く()
        {
            var gantt = await CreateGanttAsync();
            Assert.That(gantt.ViewMode, Is.EqualTo(GanttViewMode.Week));
            Assert.That(gantt.ViewStart, Is.EqualTo(SundayOf(DateTime.Today)));

            await gantt.SetViewModeAsync(GanttViewMode.Day);
            Assert.That(gantt.ViewStart, Is.EqualTo(DateTime.Today));

            await gantt.SetViewModeAsync(GanttViewMode.Month);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)));

            await gantt.SetViewModeAsync(GanttViewMode.Week);
            Assert.That(gantt.ViewStart, Is.EqualTo(SundayOf(DateTime.Today)));
        }

        [Test]
        public async Task 切り替えて戻すと元の日に戻る()
        {
            var gantt = await CreateAt1112Async();
            await gantt.SetViewModeAsync(GanttViewMode.Day);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 12)));
            for (var i = 0; i < 3; i++) await gantt.MovePageAsync(1);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 15)));

            await gantt.SetViewModeAsync(GanttViewMode.Month);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 1)));
            await gantt.SetViewModeAsync(GanttViewMode.Day);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 15)));
        }

        [Test]
        public async Task 前へは表示の単位ぶん基準日を動かす()
        {
            var gantt = await CreateAt1112Async();
            await gantt.SetViewModeAsync(GanttViewMode.Week);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 8)));

            //週: 1 週前の同じ曜日
            await gantt.MovePageAsync(-1);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 1)));
            await gantt.SetViewModeAsync(GanttViewMode.Day);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 5)));
        }

        [Test]
        public async Task 月で前へ動かしてから週へ切り替えると基準日を含む週を開く()
        {
            var gantt = await CreateAt1112Async();
            await gantt.SetViewModeAsync(GanttViewMode.Month);
            await gantt.MovePageAsync(-1);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 10, 1)));

            await gantt.SetViewModeAsync(GanttViewMode.Week);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 10, 11)));
        }

        [Test]
        public async Task スクリプトで設定した開始日はそのまま使い基準日にもなる()
        {
            var gantt = await CreateGanttAsync();
            //週表示で水曜始まり
            await gantt.SetViewStartAsync(new DateTime(2026, 11, 11));
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 11)));

            await gantt.SetViewModeAsync(GanttViewMode.Day);
            Assert.That(gantt.ViewStart, Is.EqualTo(new DateTime(2026, 11, 11)));
        }

        [Test]
        public async Task 今日ボタンは基準日を今日にする()
        {
            var gantt = await CreateAt1112Async();
            await gantt.SetViewModeAsync(GanttViewMode.Day);
            await gantt.GoTodayAsync();
            Assert.That(gantt.ViewStart, Is.EqualTo(DateTime.Today));
        }
    }
}
