using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// ボタン (またはスクリプトの Run()) でホストにジョブの実行を頼み (POST <see cref="EndPoint"/>)、終わった結果を通知で知らせる。
    /// 非同期 (IsAsynchronous) は起動したらすぐ戻り「開始しました」を通知する。結果は待たない。起動したジョブの Id は <see cref="JobId"/> / <see cref="JobIds"/> に持ち、
    /// スクリプトが <see cref="GetJobStatusAsync"/> (GetJobStatus) で状態を問い合わせる (フィールドは自分では問い合わせない)。
    /// 値を持たず、保存 (Submit) には何も載せない。
    /// </summary>
    public class JobTriggerField : FieldBase<JobTriggerFieldDesign>
    {
        public JobTriggerField(JobTriggerFieldDesign design) : base(design) { }

        /// <summary>
        /// ジョブ専用の API の URL (POST。本文は <see cref="JobRunRequest"/>、応答は <see cref="JobRunResponse"/>)。
        /// 空 (既定) なら共通の口 (<see cref="Api.ExtrasTransport"/> = ホストの 1 本の API の JobApi) を使う。ジョブ専用の Controller を持つホストが起動時に設定する。
        /// 非同期で起動したジョブの状態は GET {EndPoint}/{jobId} (応答は <see cref="JobStatusResponse"/>。知らない Id は 404) で問い合わせる。
        /// 空のまま (かつ <see cref="RunCoreAsync"/> も無い) ならジョブは動かせず、押すとその旨を通知する。
        /// </summary>
        public static string EndPoint { get; set; } = string.Empty;

        /// <summary>
        /// 依頼の送り方を差し替える (デスクトップなど HTTP を使わないホスト向け)。設定されていれば <see cref="EndPoint"/> より優先する。
        /// 通信の失敗 (理由はホストが通知済み) は null を返す。
        /// </summary>
        public static Func<JobTriggerField, JobRunRequest, Task<JobRunResponse?>>? RunCoreAsync { get; set; }

        /// <summary>
        /// 非同期で起動したジョブの状態の問い合わせ方 (<see cref="RunCoreAsync"/> の相方。引数はフィールドとジョブ Id)。
        /// <see cref="RunCoreAsync"/> を使うときは <see cref="GetJobStatusAsync"/> がこちらで問い合わせる。無ければ <see cref="GetJobStatusAsync"/> は null を返す。
        /// 知らない Id (結果を忘れた) なら null を返す。
        /// </summary>
        public static Func<JobTriggerField, string, Task<JobStatusResponse?>>? GetJobStatusCoreAsync { get; set; }

        readonly List<string> _jobIds = new();

        /// <summary>ジョブを頼んでいる間 (同期は終わるまで・非同期は起動するまで) true。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 終わったことをまだ確かめていない非同期のジョブのうち、最後に起動したものの Id (<see cref="JobIds"/> の最後。無ければ空)。
        /// <see cref="GetJobStatusAsync"/> (GetJobStatus) に渡して状態を問い合わせる。終わったと分かれば <see cref="JobIds"/> から外れるので、同じジョブを二度問い合わせない。
        /// 引数なしの GetJobStatus() が問い合わせるのは <see cref="JobIds"/> の先頭 (一番古いもの) で、これとは違う (同時実行を許さないなら同じもの)。
        /// </summary>
        public string JobId => _jobIds.LastOrDefault() ?? string.Empty;

        /// <summary>
        /// 起動して、終わったことをまだ確かめていない非同期のジョブの Id (起動順)。<see cref="GetJobStatusAsync"/> (GetJobStatus) で終わったと分かったら外れる。
        /// 呼んだ時点の写し (スクリプトで扱える型にするため配列)。
        /// </summary>
        public string[] JobIds => _jobIds.ToArray();

        /// <summary>ボタンの文言 (デザインが空なら既定)。</summary>
        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.ButtonText) ? Resources.JobTrigger_DefaultButtonText : Services.AppInfoService.Localize(Design.ButtonText);

        [ScriptHide]
        public override bool IsModified => false;
        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;
        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        /// <summary>
        /// ジョブを実行し、終わるまで待って結果を通知する (同期・非同期のどちらで動かすかはホストがデザインから読む)。
        /// 非同期 (IsAsynchronous) では起動したら「開始しました」を通知して true を返す (終わるのは待たない)。応答にジョブの Id があれば <see cref="JobId"/> と <see cref="JobIds"/> に入れる。
        /// UseSubmitData なら保存ボタンと同じく先に入力チェックを通し、だめならジョブを頼まない。
        /// 戻り値はジョブが成功したら (非同期は起動できたら) true。実行中・デザインモード・押せない状態 (IsViewOnly = 書き込み不可の権限や閲覧モード、IsEnabled が false) では何もせず false。
        /// ホストが例外を投げたらそのメッセージを通知し、null を返したら (ホストが通知済み) 何も通知しない。どちらも false。
        /// </summary>
        [ScriptName("Run")]
        public async Task<bool> RunAsync()
        {
            //他のボタンと同じく、書けない (ViewOnly) か無効なら動かさない (ホストも書き込み権限で判定する)
            if (Services.AppInfoService.IsDesignMode || IsRunning || Module == null || IsViewOnly || IsEnabled == false) return false;

            //入力チェックの間も二重に押されないよう、先に実行中にする
            IsRunning = true;
            NotifyStateChanged();
            try
            {
                var arguments = "{}";
                if (Design.UseSubmitData)
                {
                    if (!await Module.ValidateInput())
                    {
                        await Services.UIService.NotifyError(Resources.JobTrigger_InvalidInput);
                        return false;
                    }
                    arguments = CreateArguments(Module);
                }

                var request = new JobRunRequest
                {
                    ModuleName = Module.Design.Name,
                    FieldName = Design.Name,
                    Arguments = arguments,
                };
                JobRunResponse? response;
                try
                {
                    response = await SendAsync(request);
                }
                catch (Exception e)
                {
                    await Services.UIService.NotifyError(e.Message);
                    return false;
                }
                //null はホストが通知済み
                if (response == null) return false;
                if (!response.Started) return await NotifyResultAsync(response.Succeeded, response.Message, response.Error);

                await Services.UIService.NotifySuccess(string.Format(Resources.JobTrigger_StartedFormat, Design.JobName));
                if (!string.IsNullOrEmpty(response.JobId)) _jobIds.Add(response.JobId);
                return true;
            }
            finally
            {
                IsRunning = false;
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// 非同期で起動したジョブの状態を 1 回問い合わせる (フィールドは自動では呼ばない。確かめたいときにスクリプトが呼ぶ)。
        /// 終わっていれば (Status が Running でない) <see cref="JobIds"/> から外して返す。実行中ならそのまま返す。
        /// 分からない (知らない Id・問い合わせられなかった・問い合わせる口が無い・問い合わせるジョブが無い・デザインモード) なら null。通信の失敗は通知しない。
        /// サーバーが知らない Id (結果を忘れた = 二度と分からない) は <see cref="JobIds"/> から外す。通信の失敗 (次は分かるかもしれない) では外さない。
        /// </summary>
        /// <param name="jobId">ジョブの Id (<see cref="JobId"/> / <see cref="JobIds"/>)。空なら <see cref="JobIds"/> の先頭 (一番古いもの。無ければ問い合わせずに null)。</param>
        //スクリプトは省略可能引数を解決しない (本体の流儀どおりオーバーロードで出す)
        [ScriptName("GetJobStatus")]
        public async Task<JobStatusResponse?> GetJobStatusAsync(string jobId)
        {
            if (Services.AppInfoService.IsDesignMode) return null;
            if (string.IsNullOrEmpty(jobId)) jobId = _jobIds.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(jobId)) return null;
            JobStatusResponse? status;
            bool unknown;
            try
            {
                (status, unknown) = await GetJobStatusCoreOrHttpAsync(jobId);
            }
            catch (Exception)
            {
                return null;
            }
            if (unknown || (status != null && status.Status != JobStatus.Running)) _jobIds.Remove(jobId);
            return status;
        }

        /// <summary><see cref="JobIds"/> の先頭 (一番古いもの) を問い合わせる。無ければ問い合わせずに null。</summary>
        [ScriptName("GetJobStatus")]
        public async Task<JobStatusResponse?> GetJobStatusAsync() => await GetJobStatusAsync(string.Empty);

        //差し替え (RunCoreAsync) を使うホストは GetJobStatusCoreAsync (無ければ null)、ジョブ専用のルート (EndPoint) なら GET {EndPoint}/{jobId}、
        //どちらも無ければ共通の口 (ExtrasTransport) の Status。HTTP の失敗はトーストに出さず null。
        //Unknown = サーバーが知らない Id (差し替えが null・ジョブ専用ルートが 404・共通の口に届いて応答なし)
        async Task<(JobStatusResponse? Status, bool Unknown)> GetJobStatusCoreOrHttpAsync(string jobId)
        {
            if (RunCoreAsync != null)
            {
                if (GetJobStatusCoreAsync == null) return (null, false);
                var status = await GetJobStatusCoreAsync(this, jobId);
                return (status, status == null);
            }
            var http = Services.Provider?.GetService<IHttpService>();
            if (!string.IsNullOrEmpty(EndPoint))
            {
                if (http == null) return (null, false);
                HttpStatusCode? code = null;
                using var checker = http.AddChecker((c, _) => code = c);
                var result = await http.GetFromJsonAsync<JobStatusResponse>($"{EndPoint}/{Uri.EscapeDataString(jobId)}", loading: false);
                return (result, result == null && code == HttpStatusCode.NotFound);
            }
            //HTTP の失敗 (404 = 口に JobApi が無い、を含む) は Unknown にしない (トーストも出さない)
            var (response, delivered) = await ExtrasTransport.CallAsync<JobStatusResponse>(http, JobApiNames.Feature, JobApiNames.Status, new JobStatusRequest { JobId = jobId }, loading: false, onError: (_, _) => { });
            return (response, response == null && delivered);
        }

        //同期の応答の通知
        async Task<bool> NotifyResultAsync(bool succeeded, string message, string error)
        {
            if (succeeded)
            {
                await Services.UIService.NotifySuccess(string.IsNullOrWhiteSpace(message)
                    ? string.Format(Resources.JobTrigger_SucceededFormat, Design.JobName)
                    : message);
                return true;
            }
            await Services.UIService.NotifyError(string.IsNullOrWhiteSpace(error)
                ? string.Format(Resources.JobTrigger_FailedFormat, Design.JobName)
                : error);
            return false;
        }

        //差し替え (RunCoreAsync) があればそれ、ジョブ専用のルート (EndPoint) があればそこへ POST、どちらも無ければ共通の口 (ExtrasTransport) の Run。
        //送る先の無いホストでは動かせない旨を例外で (呼び出し元が通知する)
        async Task<JobRunResponse?> SendAsync(JobRunRequest request)
        {
            if (RunCoreAsync != null) return await RunCoreAsync(this, request);
            var http = Services.Provider?.GetService<IHttpService>();
            if (!string.IsNullOrEmpty(EndPoint))
            {
                if (http == null) throw new InvalidOperationException(Resources.JobTrigger_NotConfigured);
                return await http.PostAsJsonAsync<JobRunRequest, JobRunResponse>(EndPoint, request);
            }
            if (!ExtrasTransport.CanSend(http)) throw new InvalidOperationException(Resources.JobTrigger_NotConfigured);
            return (await ExtrasTransport.CallAsync<JobRunResponse>(http, JobApiNames.Feature, JobApiNames.Run, request)).Response;
        }

        //画面の内容 {"data":現在値,"submit":保存ボタンが送るのと同じもの}。送るものが無ければ (変更なし・表を持たないモジュール) submit は null
        static string CreateArguments(Module module)
        {
            var submit = module.GetSubmitData();
            var isEmpty = !submit.Delete.Any() && !submit.Update.Any() && !submit.Add.Any() && !submit.SearchDelete.Any() && submit.CurrentEditingData == null;
            var data = JsonConverterEx.SerializeObject(module.GetData(), indented: false);
            var submitJson = isEmpty ? "null" : JsonConverterEx.SerializeObject(new List<ModuleSubmitData> { submit }, indented: false);
            return "{\"data\":" + data + ",\"submit\":" + submitJson + "}";
        }
    }
}
