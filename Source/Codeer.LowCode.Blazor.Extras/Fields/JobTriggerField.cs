using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// ボタン (またはスクリプトの Run()) でホストにジョブの実行を頼み (POST <see cref="EndPoint"/>)、応答 (同期なら終わった結果・非同期なら開始) を通知で知らせる。
    /// 値を持たず、保存 (Submit) には何も載せない。
    /// </summary>
    public class JobTriggerField : FieldBase<JobTriggerFieldDesign>
    {
        public JobTriggerField(JobTriggerFieldDesign design) : base(design) { }

        /// <summary>
        /// ジョブ実行の API (POST。本文は <see cref="JobRunRequest"/>、応答は <see cref="JobRunResponse"/>)。ホストが起動時に設定する (テンプレートは "/api/job/run")。
        /// 空のまま (かつ <see cref="RunCoreAsync"/> も無い) ならジョブは動かせず、押すとその旨を通知する。
        /// </summary>
        public static string EndPoint { get; set; } = string.Empty;

        /// <summary>
        /// 依頼の送り方を差し替える (デスクトップなど HTTP を使わないホスト向け)。設定されていれば <see cref="EndPoint"/> より優先する。
        /// 通信の失敗 (理由はホストが通知済み) は null を返す。
        /// </summary>
        public static Func<JobTriggerField, JobRunRequest, Task<JobRunResponse?>>? RunCoreAsync { get; set; }

        /// <summary>ジョブの実行中 (応答を待っている間) true。</summary>
        public bool IsRunning { get; private set; }

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
        /// ジョブを実行する。同期 (既定) なら終わるまで待って結果を通知し、非同期 (IsAsynchronous) なら起動したら「開始しました」を通知してすぐ戻る (どちらで動かすかはホストがデザインから読む)。
        /// UseSubmitData なら保存ボタンと同じく先に入力チェックを通し、だめならジョブを頼まない。
        /// 戻り値は成功 (Succeeded または非同期で開始できた) なら true。実行中・デザインモード・押せない状態 (IsViewOnly = 書き込み不可の権限や閲覧モード、IsEnabled が false) では何もせず false。
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
                if (response.Started)
                {
                    await Services.UIService.NotifySuccess(string.Format(Resources.JobTrigger_StartedFormat, Design.JobName));
                    return true;
                }
                if (response.Succeeded)
                {
                    await Services.UIService.NotifySuccess(string.IsNullOrWhiteSpace(response.Message)
                        ? string.Format(Resources.JobTrigger_SucceededFormat, Design.JobName)
                        : response.Message);
                    return true;
                }
                await Services.UIService.NotifyError(string.IsNullOrWhiteSpace(response.Error)
                    ? string.Format(Resources.JobTrigger_FailedFormat, Design.JobName)
                    : response.Error);
                return false;
            }
            finally
            {
                IsRunning = false;
                NotifyStateChanged();
            }
        }

        //差し替え (RunCoreAsync) があればそれ、無ければ EndPoint へ POST。どちらも無いホストでは動かせない旨を例外で (呼び出し元が通知する)
        async Task<JobRunResponse?> SendAsync(JobRunRequest request)
        {
            if (RunCoreAsync != null) return await RunCoreAsync(this, request);
            var http = Services.Provider?.GetService<IHttpService>();
            if (http == null || string.IsNullOrEmpty(EndPoint)) throw new InvalidOperationException(Resources.JobTrigger_NotConfigured);
            return await http.PostAsJsonAsync<JobRunRequest, JobRunResponse>(EndPoint, request);
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
