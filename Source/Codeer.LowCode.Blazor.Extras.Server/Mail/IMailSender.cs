using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// メール送信インフラ。インフラごとの実装 (SMTP / Microsoft Graph / SendGrid) があり、
    /// 独自実装は <see cref="MailDispatcher"/> 経由で差し込める。
    /// </summary>
    public interface IMailSender
    {
        /// <summary>単発メッセージを送る。</summary>
        Task<MailSendResult> SendAsync(MailMessage message);

        /// <summary>
        /// 1つのテンプレートを宛先ごとの差し込み変数付きで多数へ送る。
        /// ネイティブの一斉送信 API を持つ実装 (SendGrid の personalizations) はそれに対応させ、
        /// 持たない実装は宛先ごとにテンプレートを解決して逐次送信する。
        /// 部分的な失敗は宛先ごとに報告される (それ自体では例外を投げない)。
        /// </summary>
        Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients);
    }
}
