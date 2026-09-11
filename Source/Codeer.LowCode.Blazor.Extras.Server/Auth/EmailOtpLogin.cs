using Codeer.LowCode.Blazor.Extras.Mail;
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// メールのワンタイムコードによる二要素認証の設定。appsettings のセクション名はアプリが決める (テンプレートは "EmailOtpLogin")。
    /// 有効・無効と送信先はデザインで決まる: ユーザーモジュールの LoginAccountContractField の TwoFactorEmail (送信先メールのフィールド) を設定すると有効。
    /// ここにあるのはメールの体裁と有効期限だけ。
    /// </summary>
    public class EmailOtpLoginSettings
    {
        /// <summary>送信インフラの呼び名 (MailSenderTable の名前)。空なら Mail.DefaultInfraName。</summary>
        public string MailInfraName { get; set; } = string.Empty;

        /// <summary>件名。{code} がコードに置き換わる。</summary>
        public string Subject { get; set; } = "認証コード: {code}";

        /// <summary>本文。{code} がコード、{minutes} が有効期限 (分) に置き換わる。</summary>
        public string Body { get; set; } = "ログインの認証コードは {code} です。\n{minutes} 分以内に入力してください。心当たりがない場合はこのメールを無視してください。";

        /// <summary>コードの有効期限 (分)。</summary>
        public int CodeLifetimeMinutes { get; set; } = 10;

        /// <summary>1 つのコードに対する入力の試行回数。超えるとコードは破棄され、パスワードからやり直し。</summary>
        public int MaxAttempts { get; set; } = 5;
    }

    /// <summary><see cref="EmailOtpLoginResult.Status"/> の値。</summary>
    public static class EmailOtpLoginStatus
    {
        /// <summary>検証成功。サインインしてよい。</summary>
        public const string Ok = "ok";
        /// <summary>コードを送った。メールのコードを送ってくること。</summary>
        public const string CodeRequired = "email";
        /// <summary>コード不一致 (期限切れ・試行超過を含む)。</summary>
        public const string InvalidCode = "invalid_code";
        /// <summary>メールを送れなかった (送信インフラ未設定・送信失敗)。</summary>
        public const string SendFailed = "send_failed";
    }

    /// <summary>ログインの 2 段階目の結果。</summary>
    public class EmailOtpLoginResult
    {
        public string Status { get; set; } = string.Empty;
        /// <summary>送信先を伏せ字にしたもの (t***@example.com)。画面の案内用。</summary>
        public string? MaskedEmail { get; set; }
    }

    /// <summary>
    /// ID/パスワード検証の後に呼ぶ、メールのワンタイムコード (6 桁・使い捨て) による二要素認証。
    /// コードはサーバー側のキャッシュ (IDistributedCache) に有効期限付きで置くので DB 列は要らない。送信先はユーザー行 (LoginAccountStore が読む)。
    /// TOTP (TotpLogin) と違いユーザー側の登録が要らないので導入が軽い。メールを受け取れる = 本人、という前提の強度。
    /// <code>
    /// var email = new EmailOtpLogin(SystemConfig.Instance.EmailOtpLogin, message => dispatcher.SendAsync(SystemConfig.Instance.EmailOtpLogin.MailInfraName, message), cache);
    /// var result = await email.VerifyAsync(account.UserId, account.TwoFactorEmail, loginInfo.TwoFactorCode);
    /// if (result.Status != EmailOtpLoginStatus.Ok) return Ok(result);   // email / invalid_code / send_failed: サインインしない
    /// </code>
    /// </summary>
    public class EmailOtpLogin
    {
        const string CacheKeyPrefix = "codeer.emailotp.";

        readonly EmailOtpLoginSettings _settings;
        readonly Func<MailMessage, Task<MailSendResult>> _send;
        readonly IDistributedCache _cache;

        /// <param name="send">メール送信。テンプレートは MailDispatcher (デバッグ時の宛先リダイレクト・送信インフラの解決) を通す。</param>
        public EmailOtpLogin(EmailOtpLoginSettings settings, Func<MailMessage, Task<MailSendResult>> send, IDistributedCache cache)
        {
            _settings = settings;
            _send = send;
            _cache = cache;
        }

        sealed class Pending
        {
            public string Code { get; set; } = string.Empty;
            public int Attempts { get; set; }
        }

        /// <summary>
        /// 2 段階目。code が空ならコードを発行してメールを送り、email を返す (再送 = もう一度呼ぶ。前のコードは無効になる)。
        /// code があれば検証し、成功したときだけ Ok を返す。
        /// </summary>
        public async Task<EmailOtpLoginResult> VerifyAsync(string userId, string emailAddress, string? code)
        {
            if (string.IsNullOrWhiteSpace(emailAddress)) return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.SendFailed };
            var key = CacheKeyPrefix + userId;

            if (string.IsNullOrWhiteSpace(code))
            {
                var newCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                var lifetime = TimeSpan.FromMinutes(Math.Max(1, _settings.CodeLifetimeMinutes));
                var message = new MailMessage
                {
                    To = { emailAddress },
                    Subject = Format(_settings.Subject, newCode, lifetime),
                    Body = Format(_settings.Body, newCode, lifetime),
                };
                MailSendResult result;
                try
                {
                    result = await _send(message);
                }
                catch
                {
                    return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.SendFailed };
                }
                if (!result.IsSuccess) return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.SendFailed };

                await _cache.SetStringAsync(key, JsonSerializer.Serialize(new Pending { Code = newCode }),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime });
                return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.CodeRequired, MaskedEmail = Mask(emailAddress) };
            }

            var json = await _cache.GetStringAsync(key);
            var pending = json == null ? null : JsonSerializer.Deserialize<Pending>(json);
            if (pending == null) return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.InvalidCode };

            var match = CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(pending.Code), System.Text.Encoding.ASCII.GetBytes(code.Trim()));
            if (match)
            {
                await _cache.RemoveAsync(key);
                return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.Ok };
            }

            pending.Attempts++;
            if (pending.Attempts >= Math.Max(1, _settings.MaxAttempts))
            {
                //総当たり対策: 試行超過でコードを破棄 (パスワードからやり直し = 新しいコード)
                await _cache.RemoveAsync(key);
            }
            else
            {
                //残り時間は縮めない (再設定で延びるのを避けるため sliding にはしない)
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(pending),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, _settings.CodeLifetimeMinutes)) });
            }
            return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.InvalidCode };
        }

        static string Format(string template, string code, TimeSpan lifetime)
            => template.Replace("{code}", code).Replace("{minutes}", ((int)lifetime.TotalMinutes).ToString());

        /// <summary>t***@example.com の形に伏せる。</summary>
        public static string Mask(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 0) return "***";
            var local = email[..at];
            return (local.Length <= 1 ? local : local[0] + "***") + email[at..];
        }
    }
}
