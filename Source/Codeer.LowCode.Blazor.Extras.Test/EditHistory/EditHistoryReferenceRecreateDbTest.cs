using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 物理削除したレコードの復活 (作り直し) で、他の従属レコード群の行を指す項目 (宣言の References) を作り直した行の Id に付け替える。
    /// Gantt の依存関係 (先行・後続のタスクの Id を持つ) が、作り直したタスクを指すこと。
    /// </summary>
    public class EditHistoryReferenceRecreateDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("7");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_ref_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE projects (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE project_tasks (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER, title TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE task_dependencies (id INTEGER PRIMARY KEY AUTOINCREMENT, project_id INTEGER, source_id INTEGER, destination_id INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT)", new());
            //採番を進めておく (作り直した行の Id が元の Id と重ならないように)
            await _db.ExecuteAsync(Ds, "INSERT INTO projects (id, name) VALUES (1, 'P')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO project_tasks (id, project_id, title) VALUES (1, 1, 't1'), (2, 1, 't2'), (3, 9, 'other')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO task_dependencies (id, project_id, source_id, destination_id) VALUES (1, 1, 1, 2)", new());
            _design = CreateDesign();
            _errors.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        static SearchCondition Bind(string module) => new(module)
        {
            Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Project.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
        };

        //全部物理削除 (作り直すと Id が変わる)
        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var project = new ModuleDesign { Name = "Project", DataSourceName = Ds, DbTable = "projects" };
            project.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            project.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            project.Fields.Add(new GanttFieldDesign
            {
                Name = "Gantt", TextField = "Title", IdField = "Id",
                SearchCondition = Bind("ProjectTask"),
                DependenciesModule = Bind("TaskDependency"),
                DependencySourceIdField = "Source",
                DependencyDestinationIdField = "Destination",
            });
            project.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            d.AddModule(project);

            var task = new ModuleDesign { Name = "ProjectTask", DataSourceName = Ds, DbTable = "project_tasks" };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            task.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            d.AddModule(task);

            var dependency = new ModuleDesign { Name = "TaskDependency", DataSourceName = Ds, DbTable = "task_dependencies" };
            dependency.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Project", SearchCondition = new SearchCondition("Project"), DbColumn = "project_id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Source", SearchCondition = new SearchCondition("ProjectTask"), DbColumn = "source_id" });
            dependency.Fields.Add(new LinkFieldDesign { Name = "Destination", SearchCondition = new SearchCondition("ProjectTask"), DbColumn = "destination_id" });
            d.AddModule(dependency);

            var history = new ModuleDesign { Name = "EditHistory", DataSourceName = Ds, DbTable = "edit_histories", CanCreate = false, CanUpdate = false };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new TextFieldDesign { Name = "ModuleName", DbColumn = "module_name" });
            history.Fields.Add(new TextFieldDesign { Name = "DataId", DbColumn = "data_id" });
            history.Fields.Add(new TextFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            history.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            history.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract", UserId = string.Empty, DateTime = string.Empty });
            d.AddModule(history);
            return d;
        }

        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }

        [Test]
        public async Task 作り直しでは依存関係が指すタスクを作り直したタスクのIdに付け替える()
        {
            //削除 (削除の版に、タスク 1・2 と依存関係 1→2 が入る)。Gantt の子は親の削除では消えないので、消しておく
            var deleted = await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Project", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Project", Id = "1" }],
            }]);
            Assert.That(deleted.All(e => string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, string.Join("\n", deleted.Select(e => e.ExceptionMessage)));
            await _db.ExecuteAsync(Ds, "DELETE FROM project_tasks WHERE project_id = 1", new());
            await _db.ExecuteAsync(Ds, "DELETE FROM task_dependencies WHERE project_id = 1", new());

            var deleteRow = (await _db.QueryAsync(Ds, "SELECT id FROM edit_histories WHERE change_type = 'Delete'", new())).Single();
            var submit = new ModuleSubmitData { ModuleName = "Project", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            Assert.That(results.All(e => string.IsNullOrEmpty(e.ExceptionMessage)), Is.True, string.Join("\n", results.Select(e => e.ExceptionMessage)));
            var projectId = results[0].DestinationId;
            Assert.That(projectId, Is.Not.EqualTo("1").And.Not.Empty);

            var tasks = (await _db.QueryAsync(Ds, $"SELECT id, title FROM project_tasks WHERE project_id = {long.Parse(projectId)} ORDER BY id", new()))
                .ToDictionary(e => (string)e["title"]!, e => e["id"]!.ToString());
            Assert.That(tasks.Keys, Is.EquivalentTo(new[] { "t1", "t2" }));
            Assert.That(tasks.Values, Has.None.EqualTo("1").And.None.EqualTo("2"), "タスクは新しい Id で作り直される");

            var dependencies = await _db.QueryAsync(Ds, "SELECT project_id, source_id, destination_id FROM task_dependencies", new());
            Assert.That(dependencies.Select(e => (e["project_id"]!.ToString(), e["source_id"]!.ToString(), e["destination_id"]!.ToString())),
                Is.EqualTo(new[] { (projectId, tasks["t1"], tasks["t2"]) }), "依存関係は作り直したタスクを指す");
            Assert.That(_errors, Is.Empty);
        }
    }
}
