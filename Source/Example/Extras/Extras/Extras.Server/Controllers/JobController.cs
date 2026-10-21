using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Jobs;
using Extras.Server.Services;

namespace Extras.Server.Controllers
{
    //ジョブの実行 (JobTriggerField) の受け口。クライアントは ModuleDataService.RunJobAsync。
    //動かすジョブと待つかどうかはデザインの JobTriggerField (JobName / IsAsynchronous) で決める (クライアントからは受け取らない)。押したユーザーで別プロセスに起動する (Jobs/JobProcess)
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
            if (design.IsAsynchronous)
            {
                JobProcess.Start(design.JobName, request.Arguments, userId);
                return new JobRunResponse { Started = true };
            }
            //同期: 終了コード 0 なら成功で標準出力の最後の行を画面の通知に、それ以外は標準エラー (例外のメッセージ) を画面のエラーに
            var (exitCode, output, error) = await JobProcess.RunAsync(design.JobName, request.Arguments, userId);
            if (exitCode == 0)
            {
                return new JobRunResponse { Succeeded = true, Message = JobProcess.LastLine(output) };
            }
            JobProcess.Log?.Invoke(JobProcess.FormatFailure(design.JobName, exitCode, output, error));
            return new JobRunResponse
            {
                Succeeded = false,
                Error = string.IsNullOrWhiteSpace(error) ? $"ジョブ {design.JobName} が失敗しました (終了コード {exitCode})" : error.Trim(),
            };
        }
    }
}
