using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Jobs;
using Extras.Server.Services;

namespace Extras.Server.Controllers
{
    //ジョブの実行 (JobTriggerField) の受け口。クライアントは ModuleDataService.RunJobAsync。
    //動かすジョブと待つかどうかはデザインの JobTriggerField (JobName / IsAsynchronous) で決める (クライアントからは受け取らない)。そのフィールドに書けるユーザーが、自分の権限で動く別プロセスとして起動する (Jobs/JobProcess)
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

        //監査にはこの呼び出し 1 件 (誰が・どのモジュールのどのジョブを・成否) が載る。ジョブの中で何をしたかはジョブ本体の持ち物 (子プロセスの書き込みは属性ベースの監査に載らない)
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
            if (!JobRunner.GetJobNames().Contains(design.JobName, StringComparer.OrdinalIgnoreCase))
            {
                return Failure($"ジョブ '{design.JobName}' がありません");
            }
            var userId = await _dataService.GetCurrentUserIdAsync();
            //ここから先は DB を使わない。子プロセスを待つ間 (同期) に接続を握らないよう先にプールへ返す
            await _dataService.DbAccess.ClearAsync();
            //非同期: 起動したらすぐ返す (失敗は JobProcess.Log = サーバーのログ)
            if (design.IsAsynchronous)
            {
                JobProcess.Start(design.JobName, request.Arguments, userId);
                return new JobRunResponse { Started = true };
            }
            //同期: 終了コード 0 なら成功で標準出力の最後の行を画面の通知に、それ以外は標準エラーの 1 行目 (例外のメッセージ) を画面のエラーに (全文はログ)
            var (exitCode, output, error) = await JobProcess.RunAsync(design.JobName, request.Arguments, userId);
            if (exitCode == 0)
            {
                return new JobRunResponse { Succeeded = true, Message = JobProcess.LastLine(output) };
            }
            JobProcess.Log?.Invoke(JobProcess.FormatFailure(design.JobName, exitCode, output, error));
            return Failure(string.IsNullOrWhiteSpace(error) ? $"ジョブ {design.JobName} が失敗しました (終了コード {exitCode})" : JobProcess.FirstLine(error));
        }

        //HTTP としては成功 (200) で返す業務上の失敗。監査にも失敗として残す
        JobRunResponse Failure(string error)
        {
            _audit.Fail(error);
            return new JobRunResponse { Succeeded = false, Error = error };
        }
    }
}
