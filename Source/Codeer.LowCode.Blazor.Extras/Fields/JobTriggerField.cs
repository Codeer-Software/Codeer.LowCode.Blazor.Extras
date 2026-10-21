using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// JobTriggerField のランタイム。ボタン (またはスクリプトの <c>Job.Run()</c>) でサーバーにジョブの実行を頼み、終わるまで待って結果をトーストで知らせる。
    /// 値を持たず、保存 (Submit) には何も載せない。ジョブは別プロセスで動き、保存したいならジョブが自分で ModuleDataIO で保存する。
    /// </summary>
    public class JobTriggerField(JobTriggerFieldDesign design) : FieldBase<JobTriggerFieldDesign>(design)
    {
        /// <summary>
        /// ジョブ API のベース URL (POST {EndPoint}/run)。
        /// URL はアプリ (Controller を持つ側) の持ち物なので起動時に一度設定する (テンプレートは "/api/job")。
        /// </summary>
        [ScriptHide]
        public static string EndPoint { get; set; } = string.Empty;

        /// <summary>ジョブの実行中 (結果を待っている間) true。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>ボタンの文言 (デザインが空なら既定)。</summary>
        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.ButtonText) ? Properties.Resources.JobTrigger_DefaultButtonText : Design.ButtonText;

        //ホスト (デザイナ等) には登録されていないことがあるため任意解決
        IHttpService? Http => Services.Provider?.GetService<IHttpService>();

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? data) => Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? data) => Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        /// <summary>
        /// ジョブを実行する。同期 (既定) なら終わるまで待って結果をトースト (成功 / 失敗はジョブの標準エラー) で知らせ、非同期 (IsAsynchronous) なら起動したら「開始しました」を出してすぐ戻る。実行中・デザインモードでは何もしない。
        /// UseSubmitData なら保存ボタンと同じく先に入力チェックを通し、だめならジョブを起動しない。
        /// </summary>
        [ScriptName("Run")]
        public async Task RunAsync()
        {
            if (Services.AppInfoService.IsDesignMode || IsRunning || Module == null) return;

            string arguments = "{}";
            if (Design.UseSubmitData)
            {
                if (!await Module.ValidateInput())
                {
                    await Services.UIService.NotifyError(Properties.Resources.JobTrigger_InvalidInput);
                    return;
                }
                arguments = CreateArguments(Module);
            }

            IsRunning = true;
            NotifyStateChanged();
            try
            {
                var (response, error) = await PostAsync(new JobRunRequest { JobName = Design.JobName, Arguments = arguments, Wait = !Design.IsAsynchronous });
                if (response == null)
                {
                    await Services.UIService.NotifyError(string.Format(Properties.Resources.JobTrigger_RequestFailedFormat, Design.JobName, error));
                }
                else if (response.Started)
                {
                    await Services.UIService.NotifySuccess(string.Format(Properties.Resources.JobTrigger_StartedFormat, Design.JobName));
                }
                else if (response.ExitCode == 0)
                {
                    await Services.UIService.NotifySuccess(string.Format(Properties.Resources.JobTrigger_SucceededFormat, Design.JobName));
                }
                else
                {
                    await Services.UIService.NotifyError(string.IsNullOrWhiteSpace(response.Error)
                        ? string.Format(Properties.Resources.JobTrigger_FailedFormat, Design.JobName, response.ExitCode)
                        : response.Error);
                }
            }
            finally
            {
                IsRunning = false;
                NotifyStateChanged();
            }
        }

        //送信内容 (保存ボタンが送るのと同じもの) があれば {"submit":[...]}、無ければ (表を持たないモジュール・変更なし) 現在値 {"data":...}
        static string CreateArguments(Module module)
        {
            var submit = module.GetSubmitData();
            var isEmpty = !submit.Add.Any() && !submit.Update.Any() && !submit.Delete.Any() && !submit.SearchDelete.Any() && submit.CurrentEditingData == null;
            return isEmpty
                ? "{\"data\":" + JsonConverterEx.SerializeObject(module.GetData(), indented: false) + "}"
                : "{\"submit\":" + JsonConverterEx.SerializeObject(new List<ModuleSubmitData> { submit }, indented: false) + "}";
        }

        async Task<(JobRunResponse? Response, string Error)> PostAsync(JobRunRequest request)
        {
            var http = Http;
            if (http == null || string.IsNullOrEmpty(EndPoint)) return (null, "JobTriggerField.EndPoint is not set.");

            //通信エラーは既定のログに流さず、ジョブの失敗としてまとめて出す
            var lastError = string.Empty;
            using var checker = http.AddChecker((_, message) => lastError = message);
            var response = await http.PostAsJsonAsync<JobRunRequest, JobRunResponse>($"{EndPoint}/run", request);
            return (response, lastError);
        }
    }
}
