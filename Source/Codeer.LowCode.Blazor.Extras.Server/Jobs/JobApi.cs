using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// 共通の API で受けるジョブの操作 (JobTriggerField の実行と、非同期で起動したジョブの状態)。
    /// 動かすジョブ・待つかどうか・同じジョブを重ねて動かすかはデザインの JobTriggerField (JobName / IsAsynchronous / AllowsConcurrentRuns) で決める (クライアントからは受け取らない)。
    /// そのフィールドに書けるユーザーが、自分の権限で起動する。実行先はジョブ名ごとにホストが決める:
    /// 別プロセス (<see cref="JobProcess"/>。既定) か、サーバー内実行 (このプロセスの中でそのまま動かす。ホストが実行の口を渡したジョブ)。
    /// 待つかどうか・同時実行の判定・非同期の状態の問い合わせは、実行先によらず同じ。
    /// 監査にはこの呼び出し 1 件 (誰が・どのモジュールのどのジョブを・成否。非同期では起動の成否) が載る。ジョブの中で何をしたかはジョブ本体の持ち物。
    /// </summary>
    public sealed class JobApi : IExtrasApiHandler
    {
        readonly Func<IEnumerable<string>> _jobNames;
        readonly Func<string, bool>? _runsInServer;
        readonly Func<string, string[], string, Task<string?>>? _runInServer;

        /// <param name="jobNames">ホストが動かせるジョブの名前。無い名前は起動しない。渡した一覧をそのまま持つ (起動後に増減するジョブがあるなら Func を取るほうを使う)。</param>
        public JobApi(IReadOnlyCollection<string> jobNames) => _jobNames = () => jobNames;

        /// <param name="jobNames">ホストが動かせるジョブの名前 (テンプレの JobRunner.Names)。無い名前は起動しない。デザインの差し替えで変わるジョブがあるので、起動のたびに今の一覧を聞く。</param>
        /// <param name="runsInServer">そのジョブ (デザインの JobName) をサーバー内実行にするか。false のジョブは別プロセスで起動する。</param>
        /// <param name="runInServer">
        /// サーバー内実行の口 (ジョブ名・ジョブの引数・実行するユーザーの Id → 成功の文言。失敗は例外 = <see cref="JobMode.RunAsync"/> に渡す口と同じ約束)。
        /// 引数は画面の内容の JSON 1 つ。非同期では要求が終わった後も動くので、要求のものを使わず、受け取ったユーザーの Id で DB アクセスを組み立てる。
        /// </param>
        public JobApi(Func<IEnumerable<string>> jobNames, Func<string, bool> runsInServer, Func<string, string[], string, Task<string?>> runInServer)
        {
            _jobNames = jobNames;
            _runsInServer = runsInServer;
            _runInServer = runInServer;
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

            //ここから先はこの要求の DB を使わない (サーバー内実行もホストが自分の DB アクセスを組み立てる)。ジョブを待つ間 (同期) に接続を握らないよう先にプールへ返す (DbAccessor の DisposeAsync は ClearAsync と同じ = 接続を返すだけで、後も使える)
            await context.DbAccess.DisposeAsync();

            //実行先: ホストがサーバー内実行にしたジョブはこのプロセスの中で、それ以外は別プロセスで動かす
            var userId = context.UserId;
            Func<Task<string?>>? runInServer = _runInServer != null && _runsInServer?.Invoke(design.JobName) == true
                ? () => _runInServer(design.JobName, [request.Arguments], userId)
                : null;

            //非同期: 起動したらすぐ Id を返す (結果は Status で問い合わせる。失敗は JobProcess.LoggerFactory = サーバーのログにも出る)
            //同時実行を許さないジョブが実行中なら (null) 起動しない
            if (design.IsAsynchronous)
            {
                var jobId = runInServer != null
                    ? await JobProcess.StartInServerAsync(design.JobName, runInServer, userId, design.AllowsConcurrentRuns)
                    : await JobProcess.StartAsync(design.JobName, request.Arguments, userId, design.AllowsConcurrentRuns);
                if (jobId == null) return Failure(audit, $"ジョブ '{design.JobName}' は実行中です");
                return new JobRunResponse { Started = true, JobId = jobId };
            }

            //同期: 終わるまで待つ。成功ならジョブの戻り値を画面の通知に、失敗なら例外のメッセージを画面のエラーに (全文は JobProcess がログに出す)
            if (runInServer != null)
            {
                var outcome = await JobProcess.RunInServerAsync(design.JobName, runInServer, design.AllowsConcurrentRuns);
                if (outcome == null) return Failure(audit, $"ジョブ '{design.JobName}' は実行中です");
                return outcome.Succeeded ? new JobRunResponse { Succeeded = true, Message = outcome.Message } : Failure(audit, outcome.ErrorMessage);
            }
            var result = await JobProcess.RunAsync(design.JobName, request.Arguments, userId, design.AllowsConcurrentRuns);
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
