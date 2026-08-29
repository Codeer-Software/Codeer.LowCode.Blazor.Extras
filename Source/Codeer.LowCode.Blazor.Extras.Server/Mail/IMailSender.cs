using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
//ScriptObjects には旧 API (0.5.0 互換) の MailMessage / MailAttachment もあるので、こちらは新 API の型を使う
using MailAttachment = Codeer.LowCode.Blazor.Extras.Mail.MailAttachment;
using MailMessage = Codeer.LowCode.Blazor.Extras.Mail.MailMessage;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// メール送信インフラ。プロバイダごとの実装 (SMTP / Microsoft Graph / SendGrid / Gmail) があり、
    /// それぞれ自分のプロバイダ設定だけを受け取る。プロバイダ間の差はこのインターフェースが吸収する
    /// (製品側にプロバイダ共通の設定型は無い)。独自実装はテンプレートの対応表 (MailSenderTable) に足す。
    /// </summary>
    public interface IMailSender
    {
        /// <summary>
        /// 一斉送信1回の件数上限 (プロバイダ設定の MaxBulkCount)。超過は <see cref="MailDispatcher"/> がエラーにする。
        /// </summary>
        int MaxBulkCount { get; }

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
