using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Jobs;
using Excel.Report.PDF;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// デザインの Job スクリプト (Jobs/名前.job.cs) を、ここのジョブの約束 (戻り値 = 成功の文言・失敗は例外) で動かす。
    /// ホストは、自分のジョブに無い名前をここへ渡すだけでよい (<see cref="JobMode.RunAsync"/> に渡す口・<see cref="JobApi"/> のサーバー内実行の口のどちらからでも)。
    /// スクリプトの Logger は実行先に合わせて出す: ジョブモード (別プロセス) では標準出力、サーバーの中では <see cref="JobProcess.LoggerFactory"/>。
    /// </summary>
    public static class JobScripts
    {
        /// <summary>
        /// デザインにある Job スクリプトの名前。デザインを読めないとき (まだ配置されていない・差し替え中) は空を返す (ジョブの一覧を出すところでは落とさない)。
        /// </summary>
        public static IReadOnlyList<string> GetNames(Func<DesignData> getDesignData)
        {
            try
            {
                return JobScriptRunner.GetJobNames(getDesignData());
            }
            catch
            {
                return [];
            }
        }

        /// <summary>
        /// Job スクリプトを 1 つ動かす。読み書きは渡した ModuleDataIO を通る (権限はその ModuleDataIO のユーザー。実行ごとに作ったものを渡す)。
        /// </summary>
        /// <param name="jobName">Job スクリプトの名前 (大文字小文字は区別しない)。</param>
        /// <param name="args">ジョブの引数 (ジョブ名の後ろ)。</param>
        /// <param name="getResource">スクリプトの Resources の読み出し (デザインのリソースのパス → 内容。無ければ null)。渡さなければ Resources は何も読めない。</param>
        /// <returns>成功したときの文言 (スクリプトの Run の戻り値)。失敗 (デザインに無い名前・スクリプトのエラー・Fail) は例外。</returns>
        public static async Task<string?> RunAsync(ModuleDataIO moduleDataIO, DesignData designData, string jobName, string[] args, Func<string, MemoryStream?>? getResource = null)
        {
            var name = JobScriptRunner.GetJobNames(designData).FirstOrDefault(e => string.Equals(e, jobName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"ジョブ '{jobName}' がありません");
            var result = await JobScriptRunner.RunAsync(moduleDataIO, designData, name, JobArguments.Parse(args), new ScriptLogger(), getResource);
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
            return result.Message;
        }

        sealed record MailSettings(MailConfig Mail, Func<string, IMailSender?> CreateSender);

        static readonly object _sync = new();
        static MailSettings? _mailSettings;
        static bool _mailRegistered;
        static bool _excelRegistered;

        /// <summary>
        /// Job スクリプトから MailService.Send でメールを送れるようにする (プロセスの起動時に呼ぶ)。
        /// 送信は画面のメールと同じ組み立て (送信インフラは mail の DefaultInfraName・DebugRedirectAllTo・HistoryModuleName の履歴)。履歴は Job のトランザクションで書く。
        /// 何度呼んでも登録は 1 回 (設定は最後に渡したもの)。呼ばなければ Job スクリプトに MailService は無い。
        /// </summary>
        /// <param name="mail">appsettings の Mail。</param>
        /// <param name="createSender">送信先の呼び名 → 送信インフラ (テンプレの MailSenderTable.Create)。</param>
        public static void UseMail(MailConfig mail, Func<string, IMailSender?> createSender)
        {
            ArgumentNullException.ThrowIfNull(mail);
            ArgumentNullException.ThrowIfNull(createSender);
            lock (_sync)
            {
                _mailSettings = new(mail, createSender);
                if (_mailRegistered) return;
                _mailRegistered = true;
            }
            JobScriptRunner.AddScriptTypes(m => JobScriptTypes.AddMail(m, new MailService(SendMailAsync)));
        }

        /// <summary>
        /// Job スクリプトで Excel (テンプレートへの書き込み・メールの添付。PDF は Excel.Report.PDF で変換) を使えるようにする (プロセスの起動時に呼ぶ)。
        /// Excel.ConvertPdf が未設定ならサーバーの変換を入れる (PDF のフォントはホストが設定する)。何度呼んでも登録は 1 回。
        /// </summary>
        public static void UseExcel()
        {
            lock (_sync)
            {
                ScriptObjects.Excel.ConvertPdf ??= stream => ExcelConverter.ConvertToPdf(stream);
                if (_excelRegistered) return;
                _excelRegistered = true;
            }
            JobScriptRunner.AddScriptTypes(JobScriptTypes.AddExcel);
        }

        //テスト用: UseMail / UseExcel を呼ぶ前に戻す (本体の登録は本体の側で消す)
        internal static void ResetForTest()
        {
            lock (_sync)
            {
                _mailSettings = null;
                _mailRegistered = false;
                _excelRegistered = false;
            }
        }

        static async Task<MailSendResult> SendMailAsync(RequestInterfaces.Services services, MailMessage message)
        {
            MailSettings settings;
            lock (_sync) settings = _mailSettings!;
            var moduleDataIO = JobScriptRunner.GetModuleDataIO(services)
                ?? throw new InvalidOperationException("MailService は Job スクリプトの実行の中でだけ使えます");
            //差出人は送信インフラ設定のシステム送信者 (画面からの送信と同じ。スクリプトの From は使わない)
            message.From = string.Empty;
            message.FromDisplayName = string.Empty;

            //履歴を書けなかったら Job を失敗にする (取り消して止める)。画面からの送信は送信を失敗にしないが、
            //Job は続けて書き込むので、続けると記録が欠けたまま確定する (PostgreSQL は失敗した文でトランザクションごと使えなくなり、後の書き込みが消える)
            string? historyError = null;
            var dispatcher = MailApi.CreateDispatcher(settings.Mail, settings.CreateSender,
                async data =>
                {
                    try
                    {
                        return await moduleDataIO.CreateSystemAccess().AddAsync(data);
                    }
                    catch (Exception ex)
                    {
                        historyError ??= ex.Message;
                        throw;
                    }
                },
                services.AppInfoService.GetDesignData(),
                e =>
                {
                    historyError ??= e;
                    _ = services.Logger.Error(e);
                });
            var result = await dispatcher.SendAsync(null, message);
            if (historyError != null)
            {
                var sent = result.SuccessCount > 0 ? "メールは送信しましたが、" : "メールの送信は失敗し、";
                throw new InvalidOperationException($"{sent}送信履歴を書けませんでした: {historyError}");
            }
            return result;
        }

        class ScriptLogger : RequestInterfaces.ILogger
        {
            //ジョブモードの標準出力は親 (JobProcess) がログとして受ける。サーバーの中ではアプリのログへ
            readonly ILogger? _logger = JobMode.IsRunning ? null : JobProcess.LoggerFactory?.CreateLogger(typeof(JobScripts).FullName!);

            public Task Log(string message) => Write(LogLevel.Information, message, message);
            public Task Warn(string message) => Write(LogLevel.Warning, message, $"warn: {message}");
            public Task Error(string message) => Write(LogLevel.Error, message, $"error: {message}");

            Task Write(LogLevel level, string message, string consoleLine)
            {
                if (JobMode.IsRunning) Console.WriteLine(consoleLine);
                else _logger?.Log(level, "{Message}", message);
                return Task.CompletedTask;
            }
        }
    }
}
