using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.JobTrigger;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// 共通の API の Job.Run (JobTriggerApi)。ジョブをどこで動かすかはホストが渡す実行の口が決める。
    /// このテストの実行の口は、InProcessJob をこのプロセスの中のデリゲートで、それ以外を JobRuntime.RunInNewProcessAsync (テスト用ホスト Codeer.LowCode.Blazor.Extras.Test.JobHost) で動かす。
    /// 名前だけ渡すコンストラクタは全部 JobRuntime.RunInNewProcessAsync。
    /// モジュール Item の JobTriggerField: "InProcess" (ジョブ InProcessJob・同期) / "InProcessAsync" (同じジョブ・非同期) / "Process" (ジョブ Echo・同期) / "EchoAsync" (Echo・非同期) /
    /// "Fail" (ジョブ Fail・同期) / "FailAsync" (Fail・非同期) / "Sleep" (ジョブ Sleep・同期) / "SleepAsync" (Sleep・非同期)。どれも同時実行を許さない (既定)。権限の条件は無し。
    /// JobTriggerTracker の static (Cache・LoggerFactory・実行中の数) を使うので直列で動かす。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobTriggerApiTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        DesignData _designData = null!;
        string _entryDll = null!;
        string _hostExecutable = null!;
        string _dotnetPath = null!;
        ILoggerFactory? _loggerFactory;
        List<string> _logs = null!;
        List<(string Name, string Input, string UserId, JobTriggerProcessType ProcessType)> _calls = null!;
        //途中で失敗しても、このプロセスの中で待たせたジョブを後始末で終わらせる (実行中に数えたまま次のテストに残さない)
        TaskCompletionSource<JobRuntimeResult>? _gate;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"job_api_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });
            await _db.ExecuteAsync(Ds, "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Title TEXT)", new());
            _designData = CreateDesignData();
            _calls = [];
            _logs = [];

            _entryDll = JobRuntime.EntryDll;
            _hostExecutable = JobRuntime.HostExecutable;
            _dotnetPath = JobRuntime.DotnetPath;
            _loggerFactory = JobTriggerTracker.LoggerFactory;
            JobRuntime.EntryDll = HostDll;
            JobRuntime.HostExecutable = string.Empty;
            JobTriggerTracker.LoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(_logs)));
            JobTriggerTracker.Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        }

        [TearDown]
        public void TearDown()
        {
            _gate?.TrySetResult(new JobRuntimeResult());
            _gate = null;
            JobTriggerTracker.LoggerFactory?.Dispose();
            JobRuntime.EntryDll = _entryDll;
            JobRuntime.HostExecutable = _hostExecutable;
            JobRuntime.DotnetPath = _dotnetPath;
            JobTriggerTracker.LoggerFactory = _loggerFactory;
            JobTriggerTracker.Cache = null;
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
        }

        static DesignData CreateDesignData()
        {
            var d = new DesignData();
            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "Items", CanCreate = true, CanUpdate = true };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            item.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "InProcess", JobName = "InProcessJob" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "InProcessAsync", JobName = "InProcessJob", IsAsynchronous = true });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "NoSubmit", JobName = "InProcessJob", UseSubmitData = false });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "InProcessType", JobName = "InProcessJob", ProcessType = JobTriggerProcessType.InProcess });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "Process", JobName = "Echo" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "EchoAsync", JobName = "Echo", IsAsynchronous = true });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "Fail", JobName = "Fail" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "FailAsync", JobName = "Fail", IsAsynchronous = true });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "Sleep", JobName = "Sleep" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "SleepAsync", JobName = "Sleep", IsAsynchronous = true });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "Result", JobName = "Result" });
            d.AddModule(item);
            return d;
        }

        ExtrasApiContext CreateContext(string userId) => new()
        {
            ModuleDataIO = new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>())),
            DesignData = _designData,
            DbAccess = _db,
            UserId = userId,
        };

        //実行の口: InProcessJob はこのプロセスの中 (inProcess のデリゲート)・それ以外は別プロセス
        JobTriggerApi CreateApi(Func<string, string, string, JobTriggerProcessType, Task<JobRuntimeResult>>? inProcess = null)
            => new(() => ["InProcessJob", "Echo", "Fail", "Sleep"],
                (name, input, userId, processType) => string.Equals(name, "InProcessJob", StringComparison.OrdinalIgnoreCase)
                    ? (inProcess ?? RecordCall)(name, input, userId, processType)
                    : JobRuntime.RunInNewProcessAsync(name, input, userId));

        Task<JobRuntimeResult> RecordCall(string name, string input, string userId, JobTriggerProcessType processType)
        {
            lock (_calls) _calls.Add((name, input, userId, processType));
            return Task.FromResult(new JobRuntimeResult { Message = $"in process {userId}" });
        }

        static string Payload(string field, string arguments)
            => JsonConverterEx.SerializeObject(new JobTriggerRunRequest { ModuleName = "Item", FieldName = field, Arguments = arguments }, indented: false);

        static async Task<JobTriggerRunResponse> RunAsync(JobTriggerApi api, string field, string arguments, ExtrasApiContext context)
            => (JobTriggerRunResponse)(await api.ExecuteAsync(JobTriggerApiNames.Run, Payload(field, arguments), context))!;

        [Test]
        public async Task 実行の口はジョブ名と画面の内容とユーザーIdで呼ばれ口が別プロセスにしなければ子プロセスを起動しない()
        {
            //別プロセスを起動しようとすれば例外になるようにしておく
            JobRuntime.EntryDll = Path.Combine(Path.GetTempPath(), "no_such_host_" + Guid.NewGuid() + ".dll");
            JobRuntime.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());

            var ret = await RunAsync(CreateApi(), "InProcess", """{"data":{}}""", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("in process 7"));
            Assert.That(_calls, Has.Count.EqualTo(1));
            Assert.That(_calls[0].Name, Is.EqualTo("InProcessJob"));
            Assert.That(_calls[0].Input, Is.EqualTo("""{"data":{}}"""));
            Assert.That(_calls[0].UserId, Is.EqualTo("7"));
            Assert.That(_calls[0].ProcessType, Is.EqualTo(JobTriggerProcessType.NewProcess));
        }

        [Test]
        public async Task 同期で成功したら返したデータが応答のResultJsonに入り失敗と非同期の応答には無い()
        {
            var api = CreateApi((_, _, _, _) => Task.FromResult(new JobRuntimeResult { Message = "作りました", ResultJson = "{\"Total\":12800}" }));
            var ret = await RunAsync(api, "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("作りました"));
            Assert.That(ret.ResultJson, Is.EqualTo("{\"Total\":12800}"));

            //非同期: 起動の応答には無く、状態の問い合わせで返る
            ret = await RunAsync(api, "InProcessAsync", "{}", CreateContext("7"));
            Assert.That(ret.Started, Is.True);
            Assert.That(ret.ResultJson, Is.Null);
            var status = await WaitFinishedAsync(api, ret.JobId, "7");
            Assert.That(status.ResultJson, Is.EqualTo("{\"Total\":12800}"));

            //返さないジョブ・失敗したジョブ
            Assert.That((await RunAsync(CreateApi(), "InProcess", "{}", CreateContext("7"))).ResultJson, Is.Null);
            ret = await RunAsync(CreateApi((_, _, _, _) => throw new InvalidOperationException("締められません")), "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(ret.ResultJson, Is.Null);
        }

        [Test]
        public async Task 別プロセスで動かしたジョブの返したデータも応答のResultJsonに入る()
        {
            var api = new JobTriggerApi(["Result"]);
            var ret = await RunAsync(api, "Result", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.ResultJson, Is.EqualTo("{\"Total\":12800,\"Name\":\"山田\"}"));
        }

        [Test]
        public async Task 実行の口にはデザインのProcessTypeが渡る()
        {
            var ret = await RunAsync(CreateApi(), "InProcessType", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(_calls.Single().ProcessType, Is.EqualTo(JobTriggerProcessType.InProcess));
        }

        [Test]
        public async Task UseSubmitDataがfalseならクライアントが送った内容に関わらず空のJSONを渡す()
        {
            var ret = await RunAsync(CreateApi(), "NoSubmit", """{"data":{"Title":"x"}}""", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(_calls.Single().Input, Is.EqualTo("{}"));
        }

        [Test]
        public async Task 実行の口の失敗は例外のメッセージが画面のエラーになる()
        {
            var api = CreateApi((_, _, _, _) => throw new InvalidOperationException("締められません"));
            var ret = await RunAsync(api, "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("締められません"));
        }

        [Test]
        public async Task 実行の口の非同期はIdが返り起動したユーザーが状態を問い合わせられる()
        {
            var api = CreateApi();
            var ret = await RunAsync(api, "InProcessAsync", "{}", CreateContext("7"));
            Assert.That(ret.Started, Is.True);
            Assert.That(ret.JobId, Is.Not.Empty);

            var status = await WaitFinishedAsync(api, ret.JobId, "7");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Done));
            Assert.That(status.Message, Is.EqualTo("in process 7"));
            var statusPayload = JsonConverterEx.SerializeObject(new JobTriggerStatusRequest { JobId = ret.JobId }, indented: false);
            Assert.That(await api.ExecuteAsync(JobTriggerApiNames.Status, statusPayload, CreateContext("8")), Is.Null);
            Assert.That(_calls.Single().UserId, Is.EqualTo("7"));
        }

        [Test]
        public async Task 実行の口が別プロセスに回したジョブは別プロセスで動く()
        {
            var ret = await RunAsync(CreateApi(), "Process", "入力", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("7|1|入力"));
            Assert.That(_calls, Is.Empty);
        }

        [Test]
        public async Task 名前だけ渡すコンストラクタは全部別プロセスで動く()
        {
            var api = new JobTriggerApi(["InProcessJob", "Echo"]);
            var ret = await RunAsync(api, "Process", "入力", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("7|1|入力"));

            //InProcessJob もテスト用ホストに渡る (ホストに無いジョブなので子が「ありません」で終わる)
            ret = await RunAsync(api, "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'InProcessJob' がありません"));

            //デザインの ProcessType が InProcess でも別プロセス
            ret = await RunAsync(api, "InProcessType", "{}", CreateContext("7"));
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'InProcessJob' がありません"));
        }

        [Test]
        public async Task 名前だけ渡すコンストラクタ_同期の失敗は標準エラーの最初の行が画面のエラーになり標準エラーと標準出力の全文が記録される()
        {
            var api = new JobTriggerApi(["Fail"]);
            var ret = await RunAsync(api, "Fail", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("わざと失敗 (日本語)"));
            Assert.That(_logs, Has.Count.EqualTo(1));
            //JobTriggerTracker.RunAsync の記録と同じ (JobRuntimeProcessResult.ToString())
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)").And.Contain("before failure"));
        }

        [Test]
        public async Task 名前だけ渡すコンストラクタ_非同期は成功がDoneで失敗がErrorになり失敗は全文が記録される()
        {
            var api = new JobTriggerApi(["Echo", "Fail"]);
            var ok = await RunAsync(api, "EchoAsync", "入力", CreateContext("7"));
            Assert.That(ok.Started, Is.True);
            var status = await WaitFinishedAsync(api, ok.JobId, "7");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Done));
            Assert.That(status.Message, Is.EqualTo("7|1|入力"));
            Assert.That(status.Error, Is.Empty);

            var ng = await RunAsync(api, "FailAsync", "{}", CreateContext("7"));
            Assert.That(ng.Started, Is.True);
            status = await WaitFinishedAsync(api, ng.JobId, "7");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Error));
            Assert.That(status.Error, Is.EqualTo("わざと失敗 (日本語)"));
            Assert.That(status.Message, Is.Empty);
            await WaitAsync(() => _logs.Count == 1);
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)").And.Contain("before failure"));
        }

        [Test]
        public async Task 名前だけ渡すコンストラクタ_同時実行を許さないジョブは実行中なら同期も非同期も起動せず終わった後は動く()
        {
            var api = new JobTriggerApi(["Sleep"]);
            var running = await RunAsync(api, "SleepAsync", "3000", CreateContext("7"));
            Assert.That(running.Started, Is.True);

            //起動したユーザーに関わらず止まる
            var ret = await RunAsync(api, "Sleep", "10", CreateContext("8"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'Sleep' は実行中です"));
            ret = await RunAsync(api, "SleepAsync", "10", CreateContext("7"));
            Assert.That(ret.Started, Is.False);
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'Sleep' は実行中です"));

            Assert.That((await WaitFinishedAsync(api, running.JobId, "7")).Status, Is.EqualTo(JobTriggerStatus.Done));
            ret = await RunAsync(api, "Sleep", "10", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("slept"));
        }

        [Test]
        public async Task 実行の口の中で別プロセスとこのプロセスに振り分けても同じジョブ名の実行中は実行先をまたいで数える()
        {
            //実行の口は (非同期では要求から切り離して) 後から呼ばれるので、振り分けは呼ばれた時の引数で決める: 画面の内容が "in-process" ならこのプロセスの中、それ以外は別プロセス
            var gate = _gate = new TaskCompletionSource<JobRuntimeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var inProcessCalls = 0;
            var api = new JobTriggerApi(() => ["Sleep"], (name, input, userId, _) =>
            {
                if (input != "in-process") return JobRuntime.RunInNewProcessAsync(name, input, userId);
                Interlocked.Increment(ref inProcessCalls);
                return gate.Task;
            });

            //別プロセスが実行中 → このプロセスの中にも起動しない (実行の口は呼ばれない)
            var process = await RunAsync(api, "SleepAsync", "3000", CreateContext("7"));
            Assert.That(process.Started, Is.True);
            var ret = await RunAsync(api, "Sleep", "in-process", CreateContext("7"));
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'Sleep' は実行中です"));
            ret = await RunAsync(api, "SleepAsync", "in-process", CreateContext("7"));
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'Sleep' は実行中です"));
            Assert.That(inProcessCalls, Is.EqualTo(0));
            Assert.That((await WaitFinishedAsync(api, process.JobId, "7")).Status, Is.EqualTo(JobTriggerStatus.Done));

            //このプロセスの中が実行中 → 別プロセスも起動しない
            var tracked = await RunAsync(api, "SleepAsync", "in-process", CreateContext("7"));
            Assert.That(tracked.Started, Is.True);
            await WaitAsync(() => Volatile.Read(ref inProcessCalls) == 1);
            ret = await RunAsync(api, "Sleep", "10", CreateContext("7"));
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'Sleep' は実行中です"));
            ret = await RunAsync(api, "SleepAsync", "10", CreateContext("7"));
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'Sleep' は実行中です"));

            gate.SetResult(new JobRuntimeResult { Message = "in process done" });
            var status = await WaitFinishedAsync(api, tracked.JobId, "7");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Done));
            Assert.That(status.Message, Is.EqualTo("in process done"));

            //終われば別プロセスで動く
            ret = await RunAsync(api, "Sleep", "10", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("slept"));
        }

        //別プロセスの起動自体の失敗 (実行ファイルが無い等) は、同期ではジョブの失敗 (画面のエラー・記録) になる。実行中に数えたままにしない
        [Test]
        public async Task 起動の失敗_同期はジョブの失敗として画面のエラーになり記録される()
        {
            JobRuntime.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            var api = new JobTriggerApi(["Echo"]);

            var ret = await RunAsync(api, "Process", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            //画面の文言に実行ファイルのパスは出さない。原文は記録に残る
            Assert.That(ret.Error, Is.EqualTo("The job 'Echo' could not be started."));
            Assert.That(_logs, Has.Count.EqualTo(1));
            Assert.That(_logs[0], Does.StartWith("ジョブ Echo が失敗しました: ").And.Contain("Win32Exception").And.Contain("no_such_dotnet_"));

            //同時実行を許さないジョブでも 2 回目は「実行中」にならない
            ret = await RunAsync(api, "Process", "{}", CreateContext("7"));
            Assert.That(ret.Error, Is.EqualTo("The job 'Echo' could not be started."));
        }

        //非同期では、起動の失敗も API の呼び出しの例外にならず、起動済み (Id が返る) → 状態が Error
        [Test]
        public async Task 起動の失敗_非同期はIdが返り状態がErrorになり記録される()
        {
            JobRuntime.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            var api = new JobTriggerApi(["Echo"]);

            var ret = await RunAsync(api, "EchoAsync", "{}", CreateContext("7"));
            Assert.That(ret.Started, Is.True);
            Assert.That(ret.JobId, Is.Not.Empty);
            var status = await WaitFinishedAsync(api, ret.JobId, "7");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Error));
            Assert.That(status.Error, Is.EqualTo("The job 'Echo' could not be started."));
            await WaitAsync(() => _logs.Count == 1);
            Assert.That(_logs[0], Does.StartWith("ジョブ Echo が失敗しました: ").And.Contain("Win32Exception").And.Contain("no_such_dotnet_"));

            //実行中に数えたままにしない
            ret = await RunAsync(api, "EchoAsync", "{}", CreateContext("7"));
            Assert.That(ret.Started, Is.True, ret.Error);
            Assert.That((await WaitFinishedAsync(api, ret.JobId, "7")).Status, Is.EqualTo(JobTriggerStatus.Error));
        }

        [Test]
        public async Task ジョブ名の一覧は起動のたびに聞かれ構築後の変更が反映される()
        {
            var names = new List<string>();
            var asked = 0;
            var api = new JobTriggerApi(() => { asked++; return names.ToList(); },
                (name, _, _, _) => Task.FromResult(new JobRuntimeResult { Message = $"ran {name}" }));

            //構築した時点では一覧に無い
            var ret = await RunAsync(api, "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'InProcessJob' がありません"));

            //後から増えたジョブは次の起動から動く (大文字小文字は区別しない)
            names.Add("inprocessjob");
            ret = await RunAsync(api, "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("ran InProcessJob"));

            //無くなれば起動しない
            names.Clear();
            ret = await RunAsync(api, "InProcess", "{}", CreateContext("7"));
            Assert.That(ret.Succeeded, Is.False);
            Assert.That(asked, Is.EqualTo(3));
        }

        //終わるまで状態を問い合わせる (最大 30 秒)
        async Task<JobTriggerStatusResponse> WaitFinishedAsync(JobTriggerApi api, string jobId, string userId)
        {
            var statusPayload = JsonConverterEx.SerializeObject(new JobTriggerStatusRequest { JobId = jobId }, indented: false);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                var status = (JobTriggerStatusResponse?)await api.ExecuteAsync(JobTriggerApiNames.Status, statusPayload, CreateContext(userId));
                Assert.That(status, Is.Not.Null, "状態が取れない");
                if (status!.Status != JobTriggerStatus.Running) return status;
                if (DateTime.UtcNow > deadline) Assert.Fail("timeout");
                await Task.Delay(20);
            }
        }

        static async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(100);
            Assert.That(condition(), Is.True, "timeout");
        }

        //テスト用ホスト (Test.csproj の ProjectReference でこのテストの bin に写る)
        static string HostDll
        {
            get
            {
                var dll = Path.Combine(Path.GetDirectoryName(typeof(JobTriggerApiTest).Assembly.Location)!, "Codeer.LowCode.Blazor.Extras.Test.JobHost.dll");
                Assert.That(File.Exists(dll), Is.True, dll);
                return dll;
            }
        }

        class ListLoggerProvider(List<string> logs) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new ListLogger(logs);
            public void Dispose() { }

            class ListLogger(List<string> logs) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    lock (logs) logs.Add(formatter(state, exception));
                }
            }
        }
    }
}
