using System.Text.Json;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// JobTriggerField とホストのジョブ API がやり取りする DTO の往復と、ホストが受け付けるジョブ名の判定 (JobTriggerFields.IsDeclared)。
    /// ジョブをどう動かすか (JobProcess / JobRunner) はホストのテンプレのコードなのでここでは見ない。
    /// </summary>
    public class JobTriggerFieldTest
    {
        //クライアント (JobTriggerField の HttpService) とサーバー (ASP.NET Core) はどちらも System.Text.Json の Web 既定 (camelCase) でやり取りする
        static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        [Test]
        public void DtoRoundTrip()
        {
            var request = JsonSerializer.Deserialize<JobRunRequest>(
                JsonSerializer.Serialize(new JobRunRequest { JobName = "Sample", Arguments = "{\"data\":{}}" }, Web), Web)!;
            Assert.That(request.JobName, Is.EqualTo("Sample"));
            Assert.That(request.Arguments, Is.EqualTo("{\"data\":{}}"));

            var json = JsonSerializer.Serialize(new JobRunResponse { ExitCode = 2, Error = "x" }, Web);
            Assert.That(json, Is.EqualTo("{\"exitCode\":2,\"error\":\"x\",\"started\":false}"));
            var response = JsonSerializer.Deserialize<JobRunResponse>(json, Web)!;
            Assert.That(response.ExitCode, Is.EqualTo(2));
            Assert.That(response.Error, Is.EqualTo("x"));
        }

        [Test]
        public void DtoRoundTrip_Wait()
        {
            Assert.That(new JobRunRequest().Wait, Is.True, "既定は待つ");
            var request = JsonSerializer.Deserialize<JobRunRequest>(JsonSerializer.Serialize(new JobRunRequest { JobName = "Sample", Wait = false }, Web), Web)!;
            Assert.That(request.Wait, Is.False);
            var response = JsonSerializer.Deserialize<JobRunResponse>(JsonSerializer.Serialize(new JobRunResponse { Started = true }, Web), Web)!;
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
