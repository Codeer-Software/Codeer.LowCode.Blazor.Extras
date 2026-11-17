using System.Reflection;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// デザインの Job スクリプトをジョブの約束 (戻り値 = 成功の文言・失敗は例外) で動かす DesignJobs。
    /// モジュール Order (Id / Name) を SQLite のファイル DB に置き、読み書きは渡した ModuleDataIO を通す。
    /// スクリプトの Logger の行き先は JobMode.IsRunning (internal・プロセスで一度 true になると戻らない。JobModeTest が同じプロセスで JobMode.RunAsync を呼ぶ) で分かれるので、
    /// テストごとにリフレクションで決めて後始末で戻す。JobProcess.LoggerFactory も static なので直列で動かし後始末で戻す。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class DesignJobsTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        DesignData _designData = null!;
        ILoggerFactory? _loggerFactory;
        bool _isRunning;
        List<string> _logs = null!;
        List<LogLevel> _levels = null!;

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

            _loggerFactory = JobProcess.LoggerFactory;
            _isRunning = IsRunning;
            IsRunning = false;
            _logs = [];
            _levels = [];
            JobProcess.LoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(_logs, _levels)));
        }

        [TearDown]
        public void TearDown()
        {
            JobProcess.LoggerFactory?.Dispose();
            JobProcess.LoggerFactory = _loggerFactory;
            IsRunning = _isRunning;
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

        Task<string?> RunAsync(string jobName, params string[] args) => DesignJobs.RunAsync(CreateModuleDataIO(), _designData, jobName, args);

        //JobMode.IsRunning (internal・setter は private)。テストの中で実行先を切り替えるためだけに使う
        static readonly PropertyInfo IsRunningProperty = typeof(JobMode).GetProperty("IsRunning", BindingFlags.Static | BindingFlags.NonPublic)!;
        static bool IsRunning
        {
            get => (bool)IsRunningProperty.GetValue(null)!;
            set => IsRunningProperty.SetValue(null, value);
        }

        [Test]
        public async Task 成功はRunの戻り値が返り読み書きは渡したModuleDataIOを通る()
        {
            _designData.JobScripts["Count"] = """
                string Run(JobArguments args)
                {
                    var search = new ModuleSearcher<Order>();
                    var rows = search.ExecuteRaw();
                    var submit = new ModuleSubmitter();
                    var add = new Order.Data();
                    add.Name = new TextFieldData();
                    add.Name.Value = args.Options[0];
                    submit.Add(add);
                    var result = submit.Execute();
                    if (!result.Succeeded) return result.Error;
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
        public void 無い名前はInvalidOperationException()
        {
            _designData.JobScripts["Exists"] = """
                string Run(JobArguments args)
                {
                    return "x";
                }
                """;
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await RunAsync("Nope"));
            Assert.That(ex!.Message, Is.EqualTo("ジョブ 'Nope' がありません"));
        }

        [Test]
        public async Task FailはInvalidOperationExceptionで文言がメッセージになり保存は取り消される()
        {
            _designData.JobScripts["Close"] = """
                string Run(JobArguments args)
                {
                    var submit = new ModuleSubmitter();
                    var add = new Order.Data();
                    add.Name = new TextFieldData();
                    add.Name.Value = "X";
                    submit.Add(add);
                    submit.Execute();
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

        [Test]
        public async Task サーバーの中ではLoggerはJobProcessのLoggerFactoryにレベルどおり出る()
        {
            _designData.JobScripts["Logging"] = """
                string Run(JobArguments args)
                {
                    Logger.Log("ログ");
                    Logger.Warn("警告");
                    Logger.Error("エラー");
                    return "ok";
                }
                """;
            var stdout = new StringWriter();
            var original = Console.Out;
            Console.SetOut(stdout);
            try
            {
                Assert.That(await RunAsync("Logging"), Is.EqualTo("ok"));
            }
            finally
            {
                Console.SetOut(original);
            }
            Assert.That(_logs, Is.EqualTo(new[] { "ログ", "警告", "エラー" }));
            Assert.That(_levels, Is.EqualTo(new[] { LogLevel.Information, LogLevel.Warning, LogLevel.Error }));
            Assert.That(stdout.ToString(), Is.Empty);
        }

        [Test]
        public async Task サーバーの中でLoggerFactoryがnullでも落ちない()
        {
            JobProcess.LoggerFactory?.Dispose();
            JobProcess.LoggerFactory = null;
            _designData.JobScripts["Logging"] = """
                string Run(JobArguments args)
                {
                    Logger.Log("ログ");
                    Logger.Warn("警告");
                    Logger.Error("エラー");
                    return "ok";
                }
                """;
            Assert.That(await RunAsync("Logging"), Is.EqualTo("ok"));
        }

        [Test]
        public async Task ジョブモードではLoggerは標準出力に出てLoggerFactoryには出ない()
        {
            IsRunning = true;
            _designData.JobScripts["Logging"] = """
                string Run(JobArguments args)
                {
                    Logger.Log("ログ");
                    Logger.Warn("警告");
                    Logger.Error("エラー");
                    return "ok";
                }
                """;
            var stdout = new StringWriter();
            var original = Console.Out;
            Console.SetOut(stdout);
            try
            {
                Assert.That(await RunAsync("Logging"), Is.EqualTo("ok"));
            }
            finally
            {
                Console.SetOut(original);
            }
            Assert.That(stdout.ToString().ReplaceLineEndings("\n"), Is.EqualTo("ログ\nwarn: 警告\nerror: エラー\n"));
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public void GetNamesはデザインのJobスクリプトの名前を返す()
        {
            _designData.JobScripts["A"] = "void Run(JobArguments args) { }";
            _designData.JobScripts["B"] = "void Run(JobArguments args) { }";
            Assert.That(DesignJobs.GetNames(() => _designData), Is.EquivalentTo(new[] { "A", "B" }));
            Assert.That(DesignJobs.GetNames(() => new DesignData()), Is.Empty);
        }

        [Test]
        public void GetNamesはデザインを読めなければ空()
        {
            Assert.That(DesignJobs.GetNames(() => throw new IOException("まだ配置されていません")), Is.Empty);
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
            Assert.That(await DesignJobs.RunAsync(CreateModuleDataIO(), _designData, "Who", [], invokerUserId: "2"), Is.EqualTo("佐藤/鈴木"));
        }

        class ListLoggerProvider(List<string> logs, List<LogLevel> levels) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, logs, levels);
            public void Dispose() { }

            class ListLogger(string categoryName, List<string> logs, List<LogLevel> levels) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    //スクリプトの Logger はカテゴリ DesignJobs で出る
                    Assert.That(categoryName, Is.EqualTo(typeof(DesignJobs).FullName));
                    lock (logs)
                    {
                        logs.Add(formatter(state, exception));
                        levels.Add(logLevel);
                    }
                }
            }
        }
    }
}
