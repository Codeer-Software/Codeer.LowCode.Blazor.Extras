using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Jobs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// デザイナで作った Job をホストと同じ約束 (戻り値 = 成功の文言・失敗は例外) で動かす (JobScripts → 本体の JobExecutor.RunAsync)。
    /// モジュール Order (Id / Name) を SQLite のファイル DB に置き、読み書きは渡した ModuleDataIO を通す。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobScriptRunTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        DesignData _designData = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"design_jobs_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });
            await _db.ExecuteAsync(Ds, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Orders (Id, Name) VALUES (1, 'A'), (2, 'B')", new());
            _designData = CreateDesignData();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
        }

        static DesignData CreateDesignData()
        {
            var d = new DesignData();
            var order = new ModuleDesign { Name = "Order", DataSourceName = Ds, DbTable = "Orders", CanCreate = true, CanUpdate = true, CanDelete = true };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            order.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            d.AddModule(order);
            return d;
        }

        ModuleDataIO CreateModuleDataIO() => new(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        Task<string?> RunAsync(string jobName, params string[] args)
            => JobScripts.RunAsync(new JobExecutionContext { ModuleDataIO = CreateModuleDataIO(), DesignData = _designData }, jobName, args);

        [Test]
        public async Task 成功はRunの戻り値が返り読み書きは渡したModuleDataIOを通る()
        {
            _designData.JobScripts["Count"] = """
                string Run(JobArguments args)
                {
                    var search = new ModuleSearcher<Order>();
                    var rows = search.ExecuteRaw();
                    var add = new Order.Data();
                    add.Name = new TextFieldData();
                    add.Name.Value = args.Options[0];
                    DBService.Add(add);
                    return $"{rows.Count} 件";
                }
                """;
            Assert.That(await RunAsync("Count", "C"), Is.EqualTo("2 件"));
            var names = (await _db.QueryAsync(Ds, "SELECT Name FROM Orders ORDER BY Id", new())).Select(e => e["Name"]?.ToString()).ToList();
            Assert.That(names, Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public async Task voidのRunはnullが返る()
        {
            _designData.JobScripts["Nothing"] = """
                void Run(JobArguments args)
                {
                }
                """;
            Assert.That(await RunAsync("Nothing"), Is.Null);
        }

        [Test]
        public async Task 名前の大文字小文字は区別しない()
        {
            _designData.JobScripts["CloseOrders"] = """
                string Run(JobArguments args)
                {
                    return "closed";
                }
                """;
            Assert.That(await RunAsync("closeorders"), Is.EqualTo("closed"));
            Assert.That(await RunAsync("CLOSEORDERS"), Is.EqualTo("closed"));
        }

        [Test]
        public async Task FailはInvalidOperationExceptionで文言がメッセージになり保存は取り消される()
        {
            _designData.JobScripts["Close"] = """
                string Run(JobArguments args)
                {
                    var add = new Order.Data();
                    add.Name = new TextFieldData();
                    add.Name.Value = "X";
                    DBService.Add(add);
                    Fail("対象がありません");
                    return "ok";
                }
                """;
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await RunAsync("Close"));
            Assert.That(ex!.Message, Is.EqualTo("対象がありません"));
            Assert.That(Convert.ToInt32((await _db.QueryAsync(Ds, "SELECT COUNT(*) AS cnt FROM Orders", new()))[0]["cnt"]), Is.EqualTo(2));
        }

        [Test]
        public void 実行時エラーはInvalidOperationExceptionでメッセージが取れる()
        {
            _designData.JobScripts["Broken"] = """
                string Run(JobArguments args)
                {
                    string s = null;
                    return s.Length.ToString();
                }
                """;
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await RunAsync("Broken"));
            TestContext.Out.WriteLine(ex!.Message);
            Assert.That(ex.Message, Is.Not.Empty);
        }

        //ユーザーのモジュール (1 = 佐藤 / 2 = 鈴木) を置き、「実行ユーザー/起動したユーザー」の名前を返す Job を置く。ModuleDataIO のユーザーは 1
        async Task SetUpWhoJobAsync()
        {
            await _db.ExecuteAsync(Ds, "CREATE TABLE Users (Id TEXT PRIMARY KEY, Name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Users (Id, Name) VALUES ('1', '佐藤'), ('2', '鈴木')", new());
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "Users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            _designData.AddModule(user);
            _designData.AppSettings.CurrentUserModuleDesignName = "AppUser";
            _designData.JobScripts["Who"] = """
                string Run(JobArguments args)
                {
                    return CurrentUser.Name.Value + "/" + Invoker.Name.Value;
                }
                """;
        }

        [Test]
        public async Task 起動したユーザーを渡さなければInvokerはCurrentUserと同じ()
        {
            await SetUpWhoJobAsync();
            Assert.That(await RunAsync("Who"), Is.EqualTo("佐藤/佐藤"));
        }

        [Test]
        public async Task 起動したユーザーを渡すとInvokerは起動したユーザーでCurrentUserはModuleDataIOのユーザー()
        {
            await SetUpWhoJobAsync();
            Assert.That(await JobScripts.RunAsync(new JobExecutionContext { ModuleDataIO = CreateModuleDataIO(), DesignData = _designData, InvokerUserId = "2" }, "Who", []), Is.EqualTo("佐藤/鈴木"));
        }

    }
}
