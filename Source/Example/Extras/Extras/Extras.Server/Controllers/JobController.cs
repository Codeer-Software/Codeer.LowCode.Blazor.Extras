using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Designs;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Jobs;
using Extras.Server.Services;

namespace Extras.Server.Controllers
{
    //ジョブの実行 (Extras の JobTriggerField) の受け口。クライアントは JobTriggerField.EndPoint (Client.Shared/Services/ServiceInitializer.cs) への POST。
    //非同期で起動したジョブの状態は、フィールドの GetJobStatus が GET {EndPoint}/{jobId} (StatusAsync) で問い合わせる。
    //動かすジョブ・待つかどうか・同じジョブを重ねて動かすかはデザインの JobTriggerField (JobName / IsAsynchronous / AllowsConcurrentRuns) で決める (クライアントからは受け取らない)。そのフィールドに書けるユーザーが、自分の権限で動く別プロセスとして起動する (Extras.Server の JobProcess。子側は Program.cs の JobMode)
    [ApiController]
    [Route("api/job")]
    public class JobController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;
        readonly AuditContext _audit;

        public JobController(DataService dataService, AuditContext audit)
        {
            _dataService = dataService;
            _audit = audit;
        }

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        //監査にはこの呼び出し 1 件 (誰が・どのモジュールのどのジョブを・成否。非同期では起動の成否) が載る。ジョブの中で何をしたかはジョブ本体の持ち物 (子プロセスの書き込みは属性ベースの監査に載らない)
        [HttpPost("run"), Audit(AuditCategory.DataWrite)]
        public async Task<JobRunResponse> RunAsync(JobRunRequest request)
        {
            //拒否された試行にも「どのモジュールのどのジョブか」を残すため、権限検査の前に監査の対象を登録する
            var design = _dataService.Design.DesignData.Modules.Find(request.ModuleName)?.Fields.FirstOrDefault(e => e.Name == request.FieldName) as JobTriggerFieldDesign
                ?? throw new InvalidOperationException($"{request.ModuleName}.{request.FieldName} is not a JobTriggerField.");
            _audit.AddTarget(request.ModuleName, null, $"Job:{design.JobName}");
            //そのモジュールとフィールドに書けるユーザーだけ (ExecuteSqlField の実行と同じ軸。書けなければ例外)
            await _dataService.ModuleDataIO.AuthorizationChecker.CheckFieldUserWriteAsync(request.ModuleName, request.FieldName);
            //ジョブが無ければ起動しない
            if (!JobRunner.Names.Contains(design.JobName, StringComparer.OrdinalIgnoreCase))
            {
                return Failure($"ジョブ '{design.JobName}' がありません");
            }
            var userId = await _dataService.GetCurrentUserIdAsync();
            //ここから先は DB を使わない。子プロセスを待つ間 (同期) に接続を握らないよう先にプールへ返す
            await _dataService.DbAccess.ClearAsync();
            //非同期: 起動したらすぐ Id を返す (結果は StatusAsync で問い合わせる。失敗は JobProcess.LoggerFactory = サーバーのログにも出る)
            //同時実行を許さないジョブが実行中なら (null) 起動しない
            if (design.IsAsynchronous)
            {
                var jobId = await JobProcess.StartAsync(design.JobName, request.Arguments, userId, design.AllowsConcurrentRuns);
                if (jobId == null) return Failure($"ジョブ '{design.JobName}' は実行中です");
                return new JobRunResponse { Started = true, JobId = jobId };
            }
            //同期: 終わるまで待つ。成功ならジョブの戻り値を画面の通知に、失敗なら例外のメッセージを画面のエラーに (全文は JobProcess がログに出す)
            var result = await JobProcess.RunAsync(design.JobName, request.Arguments, userId, design.AllowsConcurrentRuns);
            if (result == null) return Failure($"ジョブ '{design.JobName}' は実行中です");
            return result.Succeeded ? new JobRunResponse { Succeeded = true, Message = result.Message } : Failure(result.ErrorMessage);
        }

        //非同期で起動したジョブの状態。起動したユーザーにだけ返す (知らない Id・終わってから時間が経った結果・他のユーザーのジョブは 404)
        [HttpGet("run/{jobId}")]
        public async Task<ActionResult<JobStatusResponse>> StatusAsync(string jobId)
        {
            var userId = await _dataService.GetCurrentUserIdAsync();
            var status = await JobProcess.GetStatusAsync(jobId, userId);
            return status == null ? NotFound() : status;
        }

        //HTTP としては成功 (200) で返す業務上の失敗。監査にも失敗として残す
        JobRunResponse Failure(string error)
        {
            _audit.Fail(error);
            return new JobRunResponse { Succeeded = false, Error = error };
        }
    }
}
