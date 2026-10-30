using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.Approval;
using Codeer.LowCode.Blazor.Extras.Server.Excel;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Extras.Server.AI;
using Extras.Server.Jobs;

namespace Extras.Server.Services
{
    /// <summary>
    /// Extras の機能の API (Controllers/ExtrasController.cs が受ける) に出す機能と、機能に渡すものの対応表。
    /// 機能を足すときはここに 1 行足す。並べていない機能の要求は 404。
    /// </summary>
    public static class ExtrasApiTable
    {
        public static ExtrasApi Create() => new()
        {
            Handlers =
            {
                //メール (MailField / BulkMailField / Mail スクリプトオブジェクト)。送信インフラの対応表は MailSenderTable
                new MailApi(SystemConfig.Instance.Mail, MailSenderTable.Create),
                //ジョブ (JobTriggerField)。動かせるジョブは Jobs/JobRunner
                new JobApi(JobRunner.Names),
                //承認フロー (ApprovalFlowField)。順番到達の通知メールはメンバー契約の TurnNotifyMail が設定されているときだけ
                new ApprovalApi(SystemConfig.Instance.Mail, MailSenderTable.Create),
                //AI チャット (AIChatField)。Agent は AI/AIChatAgentTable で選ばれる
                new AIChatApi(AIChatAgentTable.Service),
                //意味検索の再索引 (SemanticSearchField のスクリプト)。バックグラウンドではリクエストの HttpContext が無いので、ユーザー Id を固定した DataService を開いて渡す
                new SemanticSearchApi(SemanticSearchIndex.Service, userId =>
                {
                    var dataService = new DataService(userId);
                    return new SemanticSearchReindexScope(dataService.ModuleDataIO, dataService.DbAccess, dataService);
                }),
                //Excel → PDF (Excel スクリプトオブジェクトの DownloadPdf)
                new ExcelApi(),
                //AI 文書解析 (AITextAnalyzerField)
                new AITextAnalyzeApi(SystemConfig.Instance.AISettings),
            },
        };
    }
}
