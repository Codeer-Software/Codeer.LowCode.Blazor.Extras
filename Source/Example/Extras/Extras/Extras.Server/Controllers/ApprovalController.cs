using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Server.Approval;
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

        public ApprovalController(DataService dataService)
        {
            _dataService = dataService;
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
            => new(DesignerService.GetDesignData(), _dataService.ModuleDataIO, _dataService.DbAccess,
                data => _dataService.ModuleDataIO.AddSystemRecordAsync(data),
                data => _dataService.ModuleDataIO.UpdateSystemRecordAsync(data));
    }
}
