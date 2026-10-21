using System.Diagnostics;
using System.Text.Json;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// JobProcess: ホストのジョブモードを別プロセスで起動する (起動は差し替え) のと、JobTriggerField とホストの API がやり取りする DTO の往復。
    /// </summary>
    [NonParallelizable]
    public class JobDbTest
    {
        const string UserId = "7";
        readonly List<ProcessStartInfo> _started = new();
        (int ExitCode, string Output, string Error) _processResult;

        [SetUp]
        public void SetUp()
        {
            _started.Clear();
            _processResult = (0, string.Empty, string.Empty);
            JobProcess.StartOverride = info => { _started.Add(info); return Task.FromResult(_processResult); };
        }

        [TearDown]
        public void TearDown() => JobProcess.StartOverride = null;

        [Test]
        public async Task StartsJobModeWithJsonAndUser()
        {
            const string json = "{\"submit\":[]}";
            var result = await JobProcess.RunAsync("ImportMemo", json, UserId);

            Assert.That(result.ExitCode, Is.EqualTo(0));
            var info = _started.Single();
            Assert.That(info.FileName, Is.EqualTo(JobProcess.DotnetPath));
            Assert.That(info.ArgumentList.Skip(1), Is.EqualTo(new[] { "job", "ImportMemo", json }), "引数は JSON 1 つ");
            Assert.That(info.Environment["Jobs__UserId"], Is.EqualTo(UserId));
            Assert.That(info.StandardErrorEncoding, Is.EqualTo(System.Text.Encoding.UTF8), "ジョブモードは UTF-8 で書く");
        }

        [Test]
        public async Task ReturnsExitCodeAndError()
        {
            _processResult = (1, string.Empty, "boom\r\n");
            var result = await JobProcess.RunAsync("ImportMemo", "{}", UserId);

            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Error, Is.EqualTo("boom\r\n"));
        }

        //クライアント (JobTriggerField の HttpService) とサーバー (ASP.NET Core) はどちらも System.Text.Json の Web 既定 (camelCase) でやり取りする
        [Test]
        public void DtoRoundTrip()
        {
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            var request = JsonSerializer.Deserialize<JobRunRequest>(
                JsonSerializer.Serialize(new JobRunRequest { JobName = "Sample", Arguments = "{\"data\":{}}" }, web), web)!;
            Assert.That(request.JobName, Is.EqualTo("Sample"));
            Assert.That(request.Arguments, Is.EqualTo("{\"data\":{}}"));

            var json = JsonSerializer.Serialize(new JobRunResponse { ExitCode = 2, Error = "x" }, web);
            Assert.That(json, Is.EqualTo("{\"exitCode\":2,\"error\":\"x\",\"started\":false}"));
            var response = JsonSerializer.Deserialize<JobRunResponse>(json, web)!;
            Assert.That(response.ExitCode, Is.EqualTo(2));
            Assert.That(response.Error, Is.EqualTo("x"));
        }

        [Test]
        public async Task Start_ReturnsWithoutWaiting_LogsFailure()
        {
            var completion = new TaskCompletionSource<(int, string, string)>();
            JobProcess.StartOverride = info => { _started.Add(info); return completion.Task; };
            var logs = new List<string>();
            JobProcess.Log = logs.Add;
            try
            {
                JobProcess.Start("ImportMemo", "{}", UserId);

                //起動はしたが、終了を待たずに返っている
                Assert.That(_started.Single().ArgumentList.Skip(1), Is.EqualTo(new[] { "job", "ImportMemo", "{}" }));
                Assert.That(completion.Task.IsCompleted, Is.False);
                Assert.That(logs, Is.Empty);

                completion.SetResult((1, string.Empty, "boom\r\n"));
                for (var i = 0; i < 50 && logs.Count == 0; i++) await Task.Delay(10);
                Assert.That(logs.Single(), Is.EqualTo("ジョブ ImportMemo が失敗しました (終了コード 1): boom"));
            }
            finally
            {
                JobProcess.Log = null;
            }
        }

        [Test]
        public void Start_Success_DoesNotLog()
        {
            var logs = new List<string>();
            JobProcess.Log = logs.Add;
            try
            {
                JobProcess.Start("ImportMemo", "{}", UserId);
                Assert.That(_started, Has.Count.EqualTo(1));
                Assert.That(logs, Is.Empty);
            }
            finally
            {
                JobProcess.Log = null;
            }
        }

        [Test]
        public void DtoRoundTrip_Wait()
        {
            var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            Assert.That(new JobRunRequest().Wait, Is.True, "既定は待つ");
            var request = JsonSerializer.Deserialize<JobRunRequest>(JsonSerializer.Serialize(new JobRunRequest { JobName = "Sample", Wait = false }, web), web)!;
            Assert.That(request.Wait, Is.False);
            var response = JsonSerializer.Deserialize<JobRunResponse>(JsonSerializer.Serialize(new JobRunResponse { Started = true }, web), web)!;
            Assert.That(response.Started, Is.True);
            Assert.That(response.ExitCode, Is.EqualTo(0));
        }

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var module = new ModuleDesign { Name = "Memo" };
            module.Fields.Add(new JobTriggerFieldDesign { Name = "Job", JobName = "ImportMemo" });
            d.AddModule(module);
            d.AddModule(new ModuleDesign { Name = "Plain" });
            return d;
        }

        [Test]
        public void IsDeclared_JobNameInDesign_IgnoresCase()
            => Assert.That(JobTriggerFields.IsDeclared(CreateDesign(), "importmemo"), Is.True);

        [Test]
        public void IsDeclared_JobNameNotInDesign()
        {
            Assert.That(JobTriggerFields.IsDeclared(CreateDesign(), "Other"), Is.False);
            Assert.That(JobTriggerFields.IsDeclared(CreateDesign(), ""), Is.False);
        }
    }
}
