using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// 共通の API で受けるジョブの操作 (JobTriggerField の実行と、非同期で起動したジョブの状態)。
    /// 動かすジョブ・待つかどうか・同じジョブを重ねて動かすか・画面の内容を渡すかはデザインの JobTriggerField (JobName / IsAsynchronous / AllowsConcurrentRuns / UseSubmitData) で決める (クライアントからは受け取らない)。
    /// そのフィールドに書けるユーザーが、自分の権限で起動する。ジョブをどこで動かすかはホストが渡す実行の口の持ち物で、ここでは分岐しない
    /// (デザインの ProcessType を実行の口に渡すだけ。別プロセスで動かす口は <see cref="JobProcess.RunInNewProcessAsync"/>)。
    /// 待つかどうか・同時実行の判定・非同期の状態の問い合わせは、どこで動かしても同じ。
    /// 監査にはこの呼び出し 1 件 (誰が・どのモジュールのどのジョブを・成否。非同期では受け付けたかどうか = 起動した後の失敗は載らず、状態とサーバーのログに出る) が載る。ジョブの中で何をしたかはジョブ本体の持ち物。
    /// </summary>
    public sealed class JobApi : IExtrasApiHandler
    {
        readonly Func<IEnumerable<string>> _jobNames;
        readonly Func<string, string, string, JobTriggerProcessType, Task<string?>> _run;

        /// <summary>全部のジョブを別プロセスで動かす (<see cref="JobProcess.RunInNewProcessAsync"/>)。デザインの ProcessType は見ない (InProcess でも別プロセス)。</summary>
        /// <param name="jobNames">ホストが動かせるジョブの名前。無い名前は起動しない。渡した一覧をそのまま持つ (起動後に増減するジョブがあるなら Func を取るほうを使う)。</param>
        public JobApi(IReadOnlyCollection<string> jobNames) : this(() => jobNames, (name, input, userId, _) => JobProcess.RunInNewProcessAsync(name, input, userId)) { }

        /// <param name="jobNames">ホストが動かせるジョブの名前 (テンプレの JobRunner.Names)。無い名前は起動しない。デザインの差し替えで変わるジョブがあるので、起動のたびに今の一覧を聞く。</param>
        /// <param name="run">
        /// 実行の口 (ジョブ名・画面の内容の JSON・起動したユーザーの Id・デザインの ProcessType → 成功の文言。失敗は例外で、メッセージの最初の行が画面に出る)。
        /// どこで動かすかはこの中でホストが決める (ProcessType はデザインの指定。別プロセスなら <see cref="JobProcess.RunInNewProcessAsync"/> を呼ぶ)。
        /// 画面の内容の JSON は、デザインの UseSubmitData が false ならクライアントが何を送っても <c>{}</c>。
        /// 要求とは切り離して呼ばれ、非同期では要求が終わった後も動く。このプロセスの中で動かすなら、要求のものを使わず、受け取ったユーザーの Id で DB アクセスを組み立てる。
        /// </param>
        public JobApi(Func<IEnumerable<string>> jobNames, Func<string, string, string, JobTriggerProcessType, Task<string?>> run)
        {
            _jobNames = jobNames;
            _run = run;
        }

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
            if (!_jobNames().Contains(design.JobName, StringComparer.OrdinalIgnoreCase))
            {
                return Failure(audit, $"ジョブ '{design.JobName}' がありません");
            }

            //ここから先はこの要求の DB を使わない (このプロセスの中で動かすジョブもホストが自分の DB アクセスを組み立てる)。ジョブを待つ間 (同期) に接続を握らないよう先にプールへ返す (DbAccessor の DisposeAsync は ClearAsync と同じ = 接続を返すだけで、後も使える)
            await context.DbAccess.DisposeAsync();

            var userId = context.UserId;
            //画面の内容を渡さないデザインなら、クライアントが何を送ってきても渡さない (クライアントも {} を送るが、要求は手で作れる)
            var arguments = design.UseSubmitData ? request.Arguments : "{}";
            var processType = design.ProcessType;
            Func<Task<string?>> run = () => _run(design.JobName, arguments, userId, processType);

            //非同期: 起動したらすぐ Id を返す (結果は Status で問い合わせる。失敗は JobProcess.LoggerFactory = サーバーのログにも出る)
            //同時実行を許さないジョブが実行中なら (null) 起動しない
            if (design.IsAsynchronous)
            {
                var jobId = await JobProcess.StartTrackedAsync(design.JobName, run, userId, design.AllowsConcurrentRuns);
                if (jobId == null) return Failure(audit, $"ジョブ '{design.JobName}' は実行中です");
                return new JobRunResponse { Started = true, JobId = jobId };
            }

            //同期: 終わるまで待つ。成功ならジョブの戻り値を画面の通知に、失敗なら例外のメッセージを画面のエラーに (全文は JobProcess がログに出す)
            var outcome = await JobProcess.RunTrackedAsync(design.JobName, run, design.AllowsConcurrentRuns);
            if (outcome == null) return Failure(audit, $"ジョブ '{design.JobName}' は実行中です");
            return outcome.Succeeded ? new JobRunResponse { Succeeded = true, Message = outcome.Message } : Failure(audit, outcome.ErrorMessage);
        }

        //業務上の失敗 (HTTP としては成功)。監査にも失敗として残す
        static JobRunResponse Failure(AuditContext? audit, string error)
        {
            audit?.Fail(error);
            return new JobRunResponse { Succeeded = false, Error = error };
        }
    }
}
