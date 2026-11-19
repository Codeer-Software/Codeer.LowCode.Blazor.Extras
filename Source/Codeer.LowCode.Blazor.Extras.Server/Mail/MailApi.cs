using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Jobs;
using Codeer.LowCode.Blazor.Repository.Data;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 共通の API で受けるメールの操作 (MailField / BulkMailField / Mail スクリプトオブジェクトの送信とプレビュー)。
    /// 中身は MailDispatcher / MailBulkSearch / MailPreviewBuilder。自分の Controller で受けるときも同じクラスを同じように呼ぶ。
    /// </summary>
    public sealed class MailApi : IExtrasApiHandler
    {
        readonly MailConfig _mail;
        readonly Func<string, IMailSender?> _createSender;

        /// <param name="mail">appsettings の Mail。</param>
        /// <param name="createSender">送信先の呼び名 → 送信インフラ (テンプレの MailSenderTable.Create)。</param>
        public MailApi(MailConfig mail, Func<string, IMailSender?> createSender)
        {
            _mail = mail;
            _createSender = createSender;
        }

        public string Feature => MailApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            MailApiNames.Send or MailApiNames.SendBulkSearch => AuditCategory.Export,
            MailApiNames.Preview or MailApiNames.PreviewBulkSearch => AuditCategory.DataRead,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            switch (action)
            {
                case MailApiNames.Send:
                    return await CreateDispatcher(context).SendAsync(ExtrasApi.ReadPayload<MailSendRequest>(payload), context.ModuleDataIO);

                //一斉送信 (宛先はサーバーで検索条件から解決。読み取り権限が効き、宛先一覧はクライアントに渡らない)
                case MailApiNames.SendBulkSearch:
                    return await new MailBulkSearch(CreateDispatcher(context), context.ModuleDataIO, context.DesignData, e => LogError(context, e))
                        .SendAsync(ExtrasApi.ReadPayload<MailBulkSearchRequest>(payload));

                //プレビュー (送らずに解決結果を HTML で返す。宛先の解決は送信と同じ経路)
                case MailApiNames.Preview:
                    return await CreatePreviewBuilder(context).BuildSingleHtmlAsync(ExtrasApi.ReadPayload<MailPreviewRequest>(payload));

                case MailApiNames.PreviewBulkSearch:
                    return await CreatePreviewBuilder(context).BuildBulkHtmlAsync(ExtrasApi.ReadPayload<MailBulkSearchRequest>(payload));

                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }

        MailPreviewBuilder CreatePreviewBuilder(ExtrasApiContext context)
            => new(CreateDispatcher(context), context.ModuleDataIO, context.DesignData);

        MailDispatcher CreateDispatcher(ExtrasApiContext context)
            //履歴はシステムの記録なので、操作ユーザーの書き込み権限に依存しない口で書く
            => CreateDispatcher(_mail, _createSender, data => context.ModuleDataIO.CreateSystemAccess().AddAsync(data),
                context.DesignData, e => LogError(context, e));

        sealed record JobScriptSettings(MailConfig Mail, Func<string, IMailSender?> CreateSender);

        static readonly object _jobScriptSync = new();
        static JobScriptSettings? _jobScriptSettings;
        static bool _jobScriptRegistered;

        /// <summary>
        /// Job スクリプトから MailService.Send でメールを送れるようにする (プロセスの起動時に呼ぶ)。
        /// 送信は画面のメールと同じ組み立て (送信インフラは mail の DefaultInfraName・DebugRedirectAllTo・HistoryModuleName の履歴)。履歴は Job のトランザクションで書く。
        /// 何度呼んでも登録は 1 回 (設定は最後に渡したもの)。呼ばなければ Job スクリプトに MailService は無い。
        /// </summary>
        /// <param name="mail">appsettings の Mail。</param>
        /// <param name="createSender">送信先の呼び名 → 送信インフラ (テンプレの MailSenderTable.Create)。</param>
        public static void UseInJobScripts(MailConfig mail, Func<string, IMailSender?> createSender)
        {
            ArgumentNullException.ThrowIfNull(mail);
            ArgumentNullException.ThrowIfNull(createSender);
            lock (_jobScriptSync)
            {
                _jobScriptSettings = new(mail, createSender);
                if (_jobScriptRegistered) return;
                _jobScriptRegistered = true;
            }
            JobExecutor.AddScriptTypes(m => JobScriptTypes.AddMail(m, new MailService(SendFromJobScriptAsync)));
        }

        //テスト用: UseInJobScripts を呼ぶ前に戻す (本体の登録は本体の側で消す)
        internal static void ResetJobScriptsForTest()
        {
            lock (_jobScriptSync)
            {
                _jobScriptSettings = null;
                _jobScriptRegistered = false;
            }
        }

        static async Task<MailSendResult> SendFromJobScriptAsync(RequestInterfaces.Services services, MailMessage message)
        {
            JobScriptSettings settings;
            lock (_jobScriptSync) settings = _jobScriptSettings!;
            var moduleDataIO = JobExecutor.GetModuleDataIO(services)
                ?? throw new InvalidOperationException("MailService は Job スクリプトの実行の中でだけ使えます");
            //差出人は送信インフラ設定のシステム送信者 (画面からの送信と同じ。スクリプトの From は使わない)
            message.From = string.Empty;
            message.FromDisplayName = string.Empty;

            //履歴を書けなかったら Job を失敗にする (取り消して止める)。画面からの送信は送信を失敗にしないが、
            //Job は続けて書き込むので、続けると記録が欠けたまま確定する (PostgreSQL は失敗した文でトランザクションごと使えなくなり、後の書き込みが消える)
            string? historyError = null;
            var dispatcher = CreateDispatcher(settings.Mail, settings.CreateSender,
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

        //Job スクリプトの MailService (UseInJobScripts) と共用。addHistory = 履歴の行の追加 (採番した Id を返す)・logError = 履歴を書けなかった理由
        internal static MailDispatcher CreateDispatcher(MailConfig mail, Func<string, IMailSender?> createSender,
            Func<ModuleData, Task<string>> addHistory, DesignData designData, Action<string> logError)
        {
            var historyWriter = string.IsNullOrEmpty(mail.HistoryModuleName)
                ? null
                : new MailHistoryWriter(mail.HistoryModuleName, designData, addHistory, logError);
            return new MailDispatcher(mail, createSender, historyWriter);
        }

        static void LogError(ExtrasApiContext context, string message)
            => context.Logger?.LogError("{Error}", message);
    }
}
