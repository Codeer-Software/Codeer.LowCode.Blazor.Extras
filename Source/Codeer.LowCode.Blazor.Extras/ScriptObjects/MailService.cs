using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// 旧 (0.5.0) のメール API のスクリプトオブジェクト。スクリプトで <see cref="MailMessage"/> を組み立て、
    /// アプリの /api/mail (Extras.Server の SmtpMailService) へ POST する。0.5.0 のテンプレートとデザイン (スクリプト) を
    /// 変更なしで動かすために残している。新規は MailField / BulkMailField を使う。
    /// </summary>
    public class MailService
    {
        [ScriptInject]
        public IHttpService? Http { get; set; }

        /// <summary>
        /// 送信エンドポイント。URL はアプリ (コントローラを持つ側) が決めるので、起動時に 1 回設定する (例: ServiceInitializer)。
        /// <see cref="SendMailAsyncCore"/> が設定されているときは使わない。
        /// </summary>
        [ScriptHide]
        public static string SendMailEndPoint { get; set; } = string.Empty;

        /// <summary>ホスト側のフック。設定されていれば (デスクトップアプリ等) <see cref="SendMailEndPoint"/> を経由せず直接送る。</summary>
        [ScriptHide]
        public static Func<MailMessage, Task<bool>>? SendMailAsyncCore { get; set; }

        [ScriptName("CreateMessage")]
        public MailMessage CreateMessage() => new();

        [ScriptName("SendEmail")]
        public async Task<bool> SendEmailAsync(string address, string subject, string message)
            => await SendAsync(new MailMessage().AddTo(address).SetSubject(subject).SetBody(message));

        [ScriptName("Send")]
        public async Task<bool> SendAsync(MailMessage message)
        {
            if (SendMailAsyncCore != null) return await SendMailAsyncCore(message);
            var endPoint = SendMailEndPoint;
            if (Http == null || string.IsNullOrEmpty(endPoint)) return false;
            var ret = await Http.PostAsJsonAsync<MailMessage, ValueWrapper<bool>>(endPoint, message);
            return ret?.Value ?? false;
        }
    }
}
