using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Jobs;
using Extras.Server.Services;

namespace Extras.Server.Controllers
{
    //ジョブの実行 (JobTriggerField) の受け口。ジョブ (Jobs/) を押したユーザーで別プロセスに起動する (JobProcess。Wait なら終わるまで待つ)
    [ApiController]
    [Route("api/job")]
    public class JobController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;

        public JobController(DataService dataService)
            => _dataService = dataService;

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        [HttpPost("run"), Audit(AuditCategory.DataWrite)]
        public async Task<JobRunResponse> RunAsync(JobRunRequest request)
        {
            await _dataService.ModuleDataIO.AuthorizationChecker.CheckAppAccessAsync();
            //デザインの JobTriggerField に書いたジョブ名だけ受け付ける
            if (!JobTriggerFields.IsDeclared(_dataService.Design.DesignData, request.JobName))
                return new JobRunResponse { ExitCode = 2, Error = $"ジョブ '{request.JobName}' はデザインにありません。" };
            var userId = await _dataService.GetCurrentUserIdAsync();
            //非同期: 起動したらすぐ返す (失敗は JobProcess.Log = サーバーのログ)
            if (!request.Wait)
            {
                JobProcess.Start(request.JobName, request.Arguments, userId);
                return new JobRunResponse { Started = true };
            }
            var (exitCode, _, error) = await JobProcess.RunAsync(request.JobName, request.Arguments, userId);
            return new JobRunResponse { ExitCode = exitCode, Error = error.Trim() };
        }
    }
}
