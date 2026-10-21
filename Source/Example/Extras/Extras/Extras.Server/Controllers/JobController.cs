using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Jobs;
using Extras.Server.Services;

namespace Extras.Server.Controllers
{
    //ジョブの実行 (JobTriggerField) の受け口。クライアントは ModuleDataService.RunJobAsync。
    //動かすジョブはデザインの JobTriggerField の JobName (クライアントからジョブ名は受け取らない)。押したユーザーで別プロセスに起動する (Jobs/JobProcess)
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
            //そのモジュールとフィールドを読めるユーザーだけ (読めなければ例外)
            var design = await _dataService.ModuleDataIO.AuthorizationChecker.CheckFieldUserReadAsync(request.ModuleName, request.FieldName) as JobTriggerFieldDesign
                ?? throw new InvalidOperationException($"{request.ModuleName}.{request.FieldName} is not a JobTriggerField.");
            var userId = await _dataService.GetCurrentUserIdAsync();
            //非同期: 起動したらすぐ返す (失敗は JobProcess.Log = サーバーのログ)
            if (!request.Wait)
            {
                JobProcess.Start(design.JobName, request.Arguments, userId);
                return new JobRunResponse { Started = true };
            }
            var (exitCode, _, error) = await JobProcess.RunAsync(design.JobName, request.Arguments, userId);
            return new JobRunResponse { ExitCode = exitCode, Error = error.Trim() };
        }
    }
}
