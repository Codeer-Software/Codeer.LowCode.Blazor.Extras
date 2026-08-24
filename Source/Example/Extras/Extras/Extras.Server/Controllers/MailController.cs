using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Microsoft.AspNetCore.Mvc;
using Extras.Server.Services;
using System.Security.Claims;

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

        //一斉送信(宛先はサーバーで検索条件から解決。読み取り権限が効き、宛先一覧はクライアントに渡らない)
        //BulkMailFieldのサマリ書き戻しは履歴と同じくシステムの記録なので内部経路で書く
        [HttpPost("bulk_search")]
        public async Task<MailSendResult> SendBulkSearchAsync(MailBulkSearchRequest request)
            => await MailBulkSearch.SendAsync(CreateDispatcher(), _dataService.ModuleDataIO,
                DesignerService.GetDesignData(), request,
                data => _dataService.ModuleDataIO.UpdateSystemRecordAsync(data), e => _logger.LogError("{Error}", e));

        MailDispatcher CreateDispatcher()
        {
            var mail = SystemConfig.Instance.Mail;
            //履歴はシステムの記録なので、操作ユーザーの書き込み権限に依存しない内部経路で書く
            var historyWriter = string.IsNullOrEmpty(mail.HistoryModuleName)
                ? null
                : new MailHistoryWriter(mail.HistoryModuleName, DesignerService.GetDesignData(),
                    data => _dataService.ModuleDataIO.AddSystemRecordAsync(data), e => _logger.LogError("{Error}", e));
            return new MailDispatcher(mail, CreateSender, historyWriter, CreateCurrentUserResolver());
        }

        //呼び名→送信インフラの対応表は MailSenderTable (独自インフラはそこに足す)
        IMailSender? CreateSender(string name)
            => MailSenderTable.Create(name, CreateUserTokenResolver());

        //「自分を差出人にする」(IsFromCurrentUser) の操作ユーザー解決
        //(認証ユーザーId → デザインの CurrentUser モジュールのメール/表示名)
        Func<Task<MailCurrentUser?>>? CreateCurrentUserResolver()
        {
            var mail = SystemConfig.Instance.Mail;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var store = new MailUserStore(DesignerService.GetDesignData(), mail,
                _dataService.DbAccess, e => _logger.LogError("{Error}", e));
            return () => store.FindCurrentUserAsync(userId);
        }

        //差出人ごとのユーザートークン検索 (Gmail ユーザー同意モード)。
        //トークン列は書き込み専用+暗号化のためサーバー内部の SQL (MailUserStore) で読んで復号する
        //(CurrentUser モジュールに GmailTokenField が無ければ使われない)
        Func<string, Task<string?>>? CreateUserTokenResolver()
        {
            var store = new MailUserStore(DesignerService.GetDesignData(), SystemConfig.Instance.Mail,
                _dataService.DbAccess, e => _logger.LogError("{Error}", e));
            return address => store.FindRefreshTokenAsync(address, SystemConfig.Instance.Gmail.TokenEncryptionKey);
        }
    }
}
