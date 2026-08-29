using Codeer.LowCode.Blazor.Extras.Server.Mail;

namespace Extras.Server.Services
{
    /// <summary>
    /// 「送信先の呼び名」→ 送信インフラ実装 (<see cref="IMailSender"/>) の対応表。
    /// 呼び名はフィールドの MailInfraName / appsettings の Mail.DefaultInfraName で指定する。
    /// </summary>
    /// <remarks>
    /// プロバイダごとの設定は appsettings の**独立したセクション** ("Smtp" / "Gmail") で、
    /// Program.cs が個別に読んでいる ("Mail" は製品が読む共通設定)。
    /// 独自インフラ (社内メールGW 等) を使うときは <see cref="IMailSender"/> を実装して
    /// この switch に1行足す (設定もこのアプリの appsettings に好きな形で置ける)。
    /// null を返すと「その呼び名は対応表に無い」エラーになる (黙って別のインフラで送らない)。
    /// 呼び名が空のケースはここには来ない (製品側が「呼び名未指定」エラーにする)。
    /// アプリの既定は appsettings の Mail.DefaultInfraName / DefaultBulkInfraName で決める。
    /// </remarks>
    public static class MailSenderTable
    {
        public static IMailSender? Create(string name)
        {
            var config = SystemConfig.Instance;
            return name switch
            {
                "Smtp" => new SmtpMailSender(config.Smtp),
                "Gmail" => new GmailApiMailSender(config.Gmail),
                _ => null,
            };
        }
    }
}
