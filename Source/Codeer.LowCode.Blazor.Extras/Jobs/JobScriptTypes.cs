using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>
    /// Job スクリプト (サーバーで動くスクリプト) で使える Extras の型・サービス。
    /// サーバー (Extras.Server の MailApi.UseInJobScripts / ExcelApi.UseInJobScripts) とデザイナ (ExtrasDesignerInitializer) が同じものを登録するための 1 か所。
    /// 本体の JobExecutor.AddScriptTypes に渡す処理の中で呼ぶ (Job の登録表は実行ごとに作るので、MailService もその都度作って渡す)。
    /// </summary>
    public static class JobScriptTypes
    {
        /// <summary>メールと Excel の両方 (デザイナの設計チェック・入力補完用)。</summary>
        public static void Register(ScriptRuntimeTypeManager manager, MailService mail)
        {
            AddMail(manager, mail);
            AddExcel(manager);
        }

        /// <summary>メール: MailService と、メッセージ・結果の型。</summary>
        public static void AddMail(ScriptRuntimeTypeManager manager, MailService mail)
        {
            manager.AddService(mail);
            manager.AddType<MailMessage>();
            manager.AddType<MailAttachment>();
            manager.AddType<MailSendResult>();
            manager.AddType<MailSendFailure>();
        }

        /// <summary>Excel: テンプレートへの書き込みとメールの添付 (Excel / ExcelCellIndex)。</summary>
        public static void AddExcel(ScriptRuntimeTypeManager manager)
        {
            manager.AddType<ScriptObjects.Excel>();
            manager.AddType<ExcelCellIndex>();
        }
    }
}
