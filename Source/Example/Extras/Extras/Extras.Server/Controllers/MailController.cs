using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Services;

namespace Extras.Server.Controllers
{
    [ApiController]
    [Route("api/mail")]
    public class MailController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;
        readonly ILogger<MailController> _logger;

        public MailController(DataService dataService, ILogger<MailController> logger)
        {
            _dataService = dataService;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        //単発送信
        [HttpPost]
        public async Task<MailSendResult> SendEmailAsync(MailSendRequest request)
            => await CreateDispatcher().SendAsync(request);

        //一斉送信(クライアントで解決済みの宛先リスト)
        [HttpPost("bulk")]
        public async Task<MailSendResult> SendBulkAsync(MailBulkRequest request)
            => await CreateDispatcher().SendBulkAsync(request);

        //一斉送信(宛先はサーバーで検索条件から解決。読み取り権限が効き、宛先一覧はクライアントに渡らない)
        [HttpPost("bulk_search")]
        public async Task<MailSendResult> SendBulkSearchAsync(MailBulkSearchRequest request)
            => await MailBulkSearch.SendAsync(CreateDispatcher(), _dataService.ModuleDataIO,
                DesignerService.GetDesignData(), SystemConfig.Instance.Mail, request);

        MailDispatcher CreateDispatcher()
        {
            var mail = SystemConfig.Instance.Mail;
            //履歴はシステムの記録なので、操作ユーザーの書き込み権限に依存しない内部経路で書く
            var historyWriter = string.IsNullOrEmpty(mail.HistoryModuleName)
                ? null
                : new MailHistoryWriter(mail.HistoryModuleName, DesignerService.GetDesignData(),
                    data => _dataService.ModuleDataIO.AddSystemRecordAsync(data), e => _logger.LogError("{Error}", e));
            return new MailDispatcher(mail, historyWriter: historyWriter);
        }
    }
}
