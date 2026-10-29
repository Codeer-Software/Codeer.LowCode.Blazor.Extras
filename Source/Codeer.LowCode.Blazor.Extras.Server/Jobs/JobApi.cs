using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// 共通の API で受けるジョブの操作 (JobTriggerField の実行と、非同期で起動したジョブの状態)。
    /// 動かすジョブ・待つかどうか・同じジョブを重ねて動かすかはデザインの JobTriggerField (JobName / IsAsynchronous / AllowsConcurrentRuns) で決める (クライアントからは受け取らない)。
    /// そのフィールドに書けるユーザーが、自分の権限で動く別プロセス (<see cref="JobProcess"/>) として起動する。
    /// 監査にはこの呼び出し 1 件 (誰が・どのモジュールのどのジョブを・成否。非同期では起動の成否) が載る。ジョブの中で何をしたかはジョブ本体の持ち物。
    /// </summary>
    public sealed class JobApi : IExtrasApiHandler
    {
        readonly IReadOnlyCollection<string> _jobNames;

        /// <param name="jobNames">ホストが動かせるジョブの名前 (テンプレの JobRunner.Names)。無い名前は起動しない。</param>
        public JobApi(IReadOnlyCollection<string> jobNames) => _jobNames = jobNames;

        public string Feature => JobApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            JobApiNames.Run => AuditCategory.DataWrite,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            //起動したユーザーとジョブを結び付ける鍵。空のまま動かすと他人のジョブの状態が見える
            if (string.IsNullOrEmpty(context.UserId)) throw new InvalidOperationException("ExtrasApiContext.UserId is required for jobs.");
            switch (action)
            {
                case JobApiNames.Run:
                    return await RunAsync(ExtrasApi.ReadPayload<JobRunRequest>(payload), context);

                //起動したユーザーにだけ返す (知らない Id・終わってから時間が経った結果・他のユーザーのジョブは応答なし)
                case JobApiNames.Status:
                    return await JobProcess.GetStatusAsync(ExtrasApi.ReadPayload<JobStatusRequest>(payload).JobId, context.UserId);

                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }

        async Task<JobRunResponse> RunAsync(JobRunRequest request, ExtrasApiContext context)
        {
            var audit = context.Audit;

            //拒否された試行にも「どのモジュールのどのジョブか」を残すため、権限検査の前に監査の対象を登録する
            var design = context.DesignData.Modules.Find(request.ModuleName)?.Fields.FirstOrDefault(e => e.Name == request.FieldName) as JobTriggerFieldDesign
                ?? throw new InvalidOperationException($"{request.ModuleName}.{request.FieldName} is not a JobTriggerField.");
            audit?.AddTarget(request.ModuleName, null, $"Job:{design.JobName}");

            //そのモジュールとフィールドに書けるユーザーだけ (ExecuteSqlField の実行と同じ軸。書けなければ例外)
            await context.ModuleDataIO.AuthorizationChecker.CheckFieldUserWriteAsync(request.ModuleName, request.FieldName);

            //ジョブが無ければ起動しない
            if (!_jobNames.Contains(design.JobName, StringComparer.OrdinalIgnoreCase))
            {
                return Failure(audit, $"ジョブ '{design.JobName}' がありません");
            }

            //ここから先は DB を使わない。子プロセスを待つ間 (同期) に接続を握らないよう先にプールへ返す (DbAccessor の DisposeAsync は ClearAsync と同じ = 接続を返すだけで、後も使える)
            await context.DbAccess.DisposeAsync();

            //非同期: 起動したらすぐ Id を返す (結果は Status で問い合わせる。失敗は JobProcess.LoggerFactory = サーバーのログにも出る)
            //同時実行を許さないジョブが実行中なら (null) 起動しない
            if (design.IsAsynchronous)
            {
                var jobId = await JobProcess.StartAsync(design.JobName, request.Arguments, context.UserId, design.AllowsConcurrentRuns);
                if (jobId == null) return Failure(audit, $"ジョブ '{design.JobName}' は実行中です");
                return new JobRunResponse { Started = true, JobId = jobId };
            }

            //同期: 終わるまで待つ。成功ならジョブの戻り値を画面の通知に、失敗なら例外のメッセージを画面のエラーに (全文は JobProcess がログに出す)
            var result = await JobProcess.RunAsync(design.JobName, request.Arguments, context.UserId, design.AllowsConcurrentRuns);
            if (result == null) return Failure(audit, $"ジョブ '{design.JobName}' は実行中です");
            return result.Succeeded ? new JobRunResponse { Succeeded = true, Message = result.Message } : Failure(audit, result.ErrorMessage);
        }

        //業務上の失敗 (HTTP としては成功)。監査にも失敗として残す
        static JobRunResponse Failure(AuditContext? audit, string error)
        {
            audit?.Fail(error);
            return new JobRunResponse { Succeeded = false, Error = error };
        }
    }
}
