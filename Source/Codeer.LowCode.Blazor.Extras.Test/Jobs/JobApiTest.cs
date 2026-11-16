using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// 共通の API の Job.Run (JobApi) の実行先。ホストが渡した runsInServer が true のジョブはサーバー内実行の口で、それ以外は別プロセス (テスト用ホスト Codeer.LowCode.Blazor.Extras.Test.JobHost) で動く。
    /// モジュール Item に JobTriggerField: "InServer" (ジョブ InServerJob・同期) / "InServerAsync" (同じジョブ・非同期) / "Process" (ジョブ Echo・同期)。権限の条件は無し。
    /// JobProcess の static (Cache・実行中の数) を使うので直列で動かす。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobApiTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        DesignData _designData = null!;
        string _entryDll = null!;
        string _hostExecutable = null!;
        string _dotnetPath = null!;
        List<(string Name, string[] Args, string UserId)> _calls = null!;

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

            _entryDll = JobProcess.EntryDll;
            _hostExecutable = JobProcess.HostExecutable;
            _dotnetPath = JobProcess.DotnetPath;
            JobProcess.EntryDll = HostDll;
            JobProcess.HostExecutable = string.Empty;
            JobProcess.Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        }

        [TearDown]
        public void TearDown()
        {
            JobProcess.EntryDll = _entryDll;
            JobProcess.HostExecutable = _hostExecutable;
            JobProcess.DotnetPath = _dotnetPath;
            JobProcess.Cache = null;
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
        }

        static DesignData CreateDesignData()
        {
            var d = new DesignData();
            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "Items", CanCreate = true, CanUpdate = true };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            item.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "InServer", JobName = "InServerJob" });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "InServerAsync", JobName = "InServerJob", IsAsynchronous = true });
            item.Fields.Add(new JobTriggerFieldDesign { Name = "Process", JobName = "Echo" });
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

        //InServerJob はサーバー内実行・Echo は別プロセス
        JobApi CreateApi(Func<string, string[], string, Task<string?>>? run = null)
            => new(() => ["InServerJob", "Echo"],
                name => string.Equals(name, "InServerJob", StringComparison.OrdinalIgnoreCase),
                run ?? ((name, args, userId) =>
                {
                    lock (_calls) _calls.Add((name, args, userId));
                    return Task.FromResult<string?>($"in server {userId}");
                }));

        static string Payload(string field, string arguments)
            => JsonConverterEx.SerializeObject(new JobRunRequest { ModuleName = "Item", FieldName = field, Arguments = arguments }, indented: false);

        [Test]
        public async Task サーバー内実行のジョブはジョブ名と引数1つとユーザーIdで口が呼ばれ子プロセスを起動しない()
        {
            //別プロセスを起動しようとすれば例外になるようにしておく
            JobProcess.EntryDll = Path.Combine(Path.GetTempPath(), "no_such_host_" + Guid.NewGuid() + ".dll");
            JobProcess.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());

            var ret = (JobRunResponse?)await CreateApi().ExecuteAsync(JobApiNames.Run, Payload("InServer", """{"data":{}}"""), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("in server 7"));
            Assert.That(_calls, Has.Count.EqualTo(1));
            Assert.That(_calls[0].Name, Is.EqualTo("InServerJob"));
            Assert.That(_calls[0].Args, Is.EqualTo(new[] { """{"data":{}}""" }));
            Assert.That(_calls[0].UserId, Is.EqualTo("7"));
        }

        [Test]
        public async Task サーバー内実行の失敗は例外のメッセージが画面のエラーになる()
        {
            var api = CreateApi((_, _, _) => throw new InvalidOperationException("締められません"));
            var ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("InServer", "{}"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("締められません"));
        }

        [Test]
        public async Task サーバー内実行の非同期はIdが返り起動したユーザーが状態を問い合わせられる()
        {
            var api = CreateApi();
            var ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("InServerAsync", "{}"), CreateContext("7"));
            Assert.That(ret!.Started, Is.True);
            Assert.That(ret.JobId, Is.Not.Empty);

            var statusPayload = JsonConverterEx.SerializeObject(new JobStatusRequest { JobId = ret.JobId }, indented: false);
            JobStatusResponse? status = null;
            for (var i = 0; i < 300; i++)
            {
                status = (JobStatusResponse?)await api.ExecuteAsync(JobApiNames.Status, statusPayload, CreateContext("7"));
                if (status?.Status != JobStatus.Running) break;
                await Task.Delay(20);
            }
            Assert.That(status!.Status, Is.EqualTo(JobStatus.Done));
            Assert.That(status.Message, Is.EqualTo("in server 7"));
            Assert.That(await api.ExecuteAsync(JobApiNames.Status, statusPayload, CreateContext("8")), Is.Null);
            Assert.That(_calls.Single().UserId, Is.EqualTo("7"));
        }

        [Test]
        public async Task サーバー内実行にしないジョブは口を呼ばず別プロセスで動く()
        {
            var ret = (JobRunResponse?)await CreateApi().ExecuteAsync(JobApiNames.Run, Payload("Process", "入力"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("7|1|入力"));
            Assert.That(_calls, Is.Empty);
        }

        [Test]
        public async Task 名前だけ渡すコンストラクタは全部別プロセスで動く()
        {
            var api = new JobApi(["InServerJob", "Echo"]);
            var ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("Process", "入力"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("7|1|入力"));

            //InServerJob もテスト用ホストに渡る (ホストに無いジョブなので子が「ありません」で終わる)
            ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("InServer", "{}"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'InServerJob' がありません"));
        }

        [Test]
        public async Task ジョブ名の一覧は起動のたびに聞かれ構築後の変更が反映される()
        {
            var names = new List<string>();
            var asked = 0;
            var api = new JobApi(() => { asked++; return names.ToList(); },
                _ => true,
                (name, _, _) => Task.FromResult<string?>($"ran {name}"));

            //構築した時点では一覧に無い
            var ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("InServer", "{}"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.False);
            Assert.That(ret.Error, Is.EqualTo("ジョブ 'InServerJob' がありません"));

            //後から増えたジョブは次の起動から動く (大文字小文字は区別しない)
            names.Add("inserverjob");
            ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("InServer", "{}"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.True, ret.Error);
            Assert.That(ret.Message, Is.EqualTo("ran InServerJob"));

            //無くなれば起動しない
            names.Clear();
            ret = (JobRunResponse?)await api.ExecuteAsync(JobApiNames.Run, Payload("InServer", "{}"), CreateContext("7"));
            Assert.That(ret!.Succeeded, Is.False);
            Assert.That(asked, Is.EqualTo(3));
        }

        //テスト用ホスト (Test.csproj の ProjectReference でこのテストの bin に写る)
        static string HostDll
        {
            get
            {
                var dll = Path.Combine(Path.GetDirectoryName(typeof(JobApiTest).Assembly.Location)!, "Codeer.LowCode.Blazor.Extras.Test.JobHost.dll");
                Assert.That(File.Exists(dll), Is.True, dll);
                return dll;
            }
        }
    }
}
