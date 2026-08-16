using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Server.Approval;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Extras.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace Extras.Server.Controllers
{
    //承認フローの command API。状態遷移の唯一の口 (クライアントは承認モジュールを直接書けない)。
    //ロジックは ApprovalEngine (Extras.Server) にあり、Controller は結線だけを持つ
    [ApiController]
    [Route("api/approval")]
    public class ApprovalController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;
        readonly ILogger<ApprovalController> _logger;

        public ApprovalController(DataService dataService, ILogger<ApprovalController> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        [HttpPost("submit")]
        public async Task<ApprovalActionResult> SubmitAsync(ApprovalSubmitRequest request)
            => await CreateEngine().SubmitAsync(request);

        [HttpPost("resubmit")]
        public async Task<ApprovalActionResult> ResubmitAsync(ApprovalSubmitRequest request)
            => await CreateEngine().ResubmitAsync(request);

        [HttpPost("approve")]
        public async Task<ApprovalActionResult> ApproveAsync(ApprovalActionRequest request)
            => await CreateEngine().ExecuteAsync(ApprovalAction.Approve.ToDesignValue(), request);

        [HttpPost("reject")]
        public async Task<ApprovalActionResult> RejectAsync(ApprovalActionRequest request)
            => await CreateEngine().ExecuteAsync(ApprovalAction.Reject.ToDesignValue(), request);

        [HttpPost("return")]
        public async Task<ApprovalActionResult> ReturnAsync(ApprovalActionRequest request)
            => await CreateEngine().ExecuteAsync(ApprovalAction.Return.ToDesignValue(), request);

        [HttpPost("withdraw")]
        public async Task<ApprovalActionResult> WithdrawAsync(ApprovalActionRequest request)
            => await CreateEngine().ExecuteAsync(ApprovalAction.Withdraw.ToDesignValue(), request);

        [HttpPost("confirm")]
        public async Task<ApprovalActionResult> ConfirmAsync(ApprovalActionRequest request)
            => await CreateEngine().ExecuteAsync(ApprovalAction.Confirm.ToDesignValue(), request);

        //承認データの書き込みはシステムの記録なので、操作ユーザーの書き込み権限に依存しない内部経路で行う
        ApprovalEngine CreateEngine()
        {
            var mail = SystemConfig.Instance.Mail;
            //送信履歴はシステムの記録なので内部経路で書く (MailController と同じ)
            var historyWriter = string.IsNullOrEmpty(mail.HistoryModuleName)
                ? null
                : new MailHistoryWriter(mail.HistoryModuleName, DesignerService.GetDesignData(),
                    data => _dataService.ModuleDataIO.AddSystemRecordAsync(data), e => _logger.LogError("{Error}", e));
            return new(DesignerService.GetDesignData(), _dataService.ModuleDataIO, _dataService.DbAccess,
                data => _dataService.ModuleDataIO.AddSystemRecordAsync(data),
                data => _dataService.ModuleDataIO.UpdateSystemRecordAsync(data))
            {
                //順番到達の通知メール (メンバー契約の TurnNotifyMail が設定されているときだけ送られる)
                MailDispatcher = new MailDispatcher(mail, historyWriter: historyWriter),
                LogError = e => _logger.LogError("{Error}", e),
            };
        }
    }
}
