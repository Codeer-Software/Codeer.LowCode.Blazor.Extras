using Codeer.LowCode.Blazor.Extras.Server.Mail;

namespace Extras.Server.Services
{
    /// <summary>
    /// 「送信先の呼び名」→ 送信インフラ実装 (<see cref="IMailSender"/>) の対応表。
    /// 呼び名はフィールドの MailInfraName / appsettings の Mail.DefaultInfraName で指定する。
    /// </summary>
    /// <remarks>
    /// プロバイダごとの設定は appsettings の**独立したセクション** ("Smtp" / "GraphApi" / "SendGrid" / "Gmail") で、
    /// Program.cs が個別に読んでいる ("Mail" は製品が読む共通設定)。
    /// 独自インフラ (社内メールGW 等) を使うときは <see cref="IMailSender"/> を実装して
    /// この switch に1行足す (設定もこのアプリの appsettings に好きな形で置ける)。
    /// null を返すと「そのインフラは未設定」エラーになる (黙って別のインフラで送らない)。
    /// </remarks>
    public static class MailSenderTable
    {
        public static IMailSender? Create(string name, Func<string, Task<string?>>? gmailUserTokenResolver = null)
        {
            var config = SystemConfig.Instance;
            return name switch
            {
                "GraphApi" => new GraphApiMailSender(config.GraphApi),
                "SendGrid" => new SendGridMailSender(config.SendGrid),
                "Gmail" => new GmailApiMailSender(config.Gmail, userRefreshTokenResolver: gmailUserTokenResolver),
                //呼び名の省略 (空) はここに来る = このアプリの既定
                "Smtp" or "" => new SmtpMailSender(config.Smtp),
                _ => null,
            };
        }
    }
}
