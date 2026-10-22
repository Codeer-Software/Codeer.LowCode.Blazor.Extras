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
        /// <summary>コード不一致 (今有効なコードと違う)。</summary>
        public const string InvalidCode = "invalid_code";
        /// <summary>有効なコードが無い (期限切れ・試行超過で破棄・使用済み)。パスワードからやり直す (新しいコードを送る)。</summary>
        public const string CodeExpired = "code_expired";
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
    /// if (result.Status != EmailOtpLoginStatus.Ok) return Ok(result);   // email / invalid_code / code_expired / send_failed: サインインしない
    /// </code>
    /// </summary>
    public class EmailOtpLogin
    {
        const string CacheKeyPrefix = "codeer.emailotp.";

        readonly EmailOtpLoginSettings _settings;
        readonly Func<MailMessage, Task<MailSendResult>> _send;
        readonly IDistributedCache _cache;
        readonly Func<DateTimeOffset> _now;

        /// <param name="send">メール送信。テンプレートは MailDispatcher (デバッグ時の宛先リダイレクト・送信インフラの解決) を通す。</param>
        public EmailOtpLogin(EmailOtpLoginSettings settings, Func<MailMessage, Task<MailSendResult>> send, IDistributedCache cache)
            : this(settings, send, cache, () => DateTimeOffset.UtcNow) { }

        internal EmailOtpLogin(EmailOtpLoginSettings settings, Func<MailMessage, Task<MailSendResult>> send, IDistributedCache cache, Func<DateTimeOffset> now)
        {
            _settings = settings;
            _send = send;
            _cache = cache;
            _now = now;
        }

        sealed class Pending
        {
            public string Code { get; set; } = string.Empty;
            public int Attempts { get; set; }
            //発行した時点で決まる期限 (打ち間違えて書き直しても延ばさない)
            public DateTimeOffset ExpiresAt { get; set; }
            //送信先のハッシュ。キーは Id だけなので、共有キャッシュを別のアプリが同じ Id で使っても、そのコードを通さない
            public string Address { get; set; } = string.Empty;
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

                var issued = new Pending { Code = newCode, ExpiresAt = _now() + lifetime, Address = AddressHash(emailAddress) };
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(issued),
                    new DistributedCacheEntryOptions { AbsoluteExpiration = issued.ExpiresAt });
                return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.CodeRequired, MaskedEmail = Mask(emailAddress) };
            }

            var json = await _cache.GetStringAsync(key);
            var pending = Read(json);
            //どのコードでも通らない状態なので不一致とは分ける (本人が期限切れのコードを入れ直し続けても総当たり対策の失敗に数えさせない)
            if (pending == null || pending.ExpiresAt <= _now() || pending.Address != AddressHash(emailAddress))
                return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.CodeExpired };

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
                //期限は発行したときのまま (書き直しで延ばさない)
                await _cache.SetStringAsync(key, JsonSerializer.Serialize(pending),
                    new DistributedCacheEntryOptions { AbsoluteExpiration = pending.ExpiresAt });
            }
            return new EmailOtpLoginResult { Status = EmailOtpLoginStatus.InvalidCode };
        }

        //読めない値 (前の版が書いた等) は有効なコードが無いものとして扱う
        static Pending? Read(string? json)
        {
            if (json == null) return null;
            try
            {
                return JsonSerializer.Deserialize<Pending>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        static string AddressHash(string emailAddress)
            => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(emailAddress.Trim().ToUpperInvariant())));

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
