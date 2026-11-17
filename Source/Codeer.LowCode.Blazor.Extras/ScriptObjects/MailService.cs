using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// Job スクリプトからメールを送るサービス (スクリプトでは <c>MailService.Send(message)</c>)。
    /// 送信の実体はサーバーのホストが渡す (Extras.Server の MailApi.UseInJobScripts)。引数なしで作ったものは設計チェック・入力補完用で、送ると例外。
    /// </summary>
    public class MailService
    {
        readonly Func<RequestInterfaces.Services, MailMessage, Task<MailSendResult>>? _send;

        /// <summary>送れないもの (デザイナの設計チェック・入力補完用)。</summary>
        public MailService() { }

        /// <param name="send">送信の実体 (スクリプトを動かしている Services と送るメッセージ)。</param>
        public MailService(Func<RequestInterfaces.Services, MailMessage, Task<MailSendResult>> send) => _send = send;

        [ScriptInject]
        public RequestInterfaces.Services? Services { get; set; }

        /// <summary>メールを送る。宛先ごとの失敗は戻り値の Failures (例外にはならない)。</summary>
        [ScriptName("Send")]
        public async Task<MailSendResult> SendAsync(MailMessage message)
        {
            if (_send == null || Services == null) throw new InvalidOperationException("このアプリではスクリプトからメールを送れません (MailService が結線されていません)");
            return await _send(Services, message);
        }
    }
}
