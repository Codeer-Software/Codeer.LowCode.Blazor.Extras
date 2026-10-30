using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Microsoft.Extensions.Caching.Distributed;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    public enum PasswordLoginStatus
    {
        /// <summary>ID / パスワードのログインが無い (ユーザーモジュールに契約が無い・契約にパスワードの列が無い)。</summary>
        NotAvailable,
        /// <summary>総当たり対策でロック中 (照合していない)。</summary>
        Locked,
        /// <summary>ログイン名が無い・停止中・パスワード不一致 (理由は分けない)。失敗として数える。</summary>
        Unauthorized,
        /// <summary>二要素認証の途中 (登録情報・コードを求めた・コード不一致・期限切れ・送れなかった)。<see cref="PasswordLoginResult.Response"/> をそのままクライアントへ返す。</summary>
        TwoFactor,
        /// <summary>照合が全部通った。<see cref="PasswordLoginResult.Account"/> でサインインする。</summary>
        Ok,
    }

    public class PasswordLoginResult
    {
        public PasswordLoginStatus Status { get; init; }

        /// <summary>サインインするアカウント。<see cref="PasswordLoginStatus.Ok"/> のときだけ (二要素認証の途中では出さない = 監査にもユーザー Id を入れない)。</summary>
        public LoginAccount? Account { get; init; }

        /// <summary>
        /// クライアント (login.js / MAUI の Login.razor) にそのまま返すもの。<see cref="PasswordLoginStatus.TwoFactor"/> では
        /// <see cref="TotpLoginResult"/> (setup / totp / invalid_code) か <see cref="EmailOtpLoginResult"/> (email / invalid_code / code_expired / send_failed)、
        /// <see cref="PasswordLoginStatus.Ok"/> では status が ok のもの。それ以外は null。
        /// </summary>
        public object? Response { get; init; }

        /// <summary>監査ログの補足 ("Locked" / "TwoFactor=setup" など)。<see cref="PasswordLoginStatus.Unauthorized"/> と <see cref="PasswordLoginStatus.Ok"/> は null。</summary>
        public string? Detail { get; init; }

        /// <summary>
        /// 監査ログで「拒否」にする回 (ロック中・二要素認証のコード不一致)。パスワード不一致 (<see cref="PasswordLoginStatus.Unauthorized"/>) は
        /// ホストが 401 を返すことで拒否になるので、ここでは立てない。
        /// </summary>
        public bool IsDenied { get; init; }
    }

    /// <summary>
    /// ID / パスワードのログインの検証の流れ。ホストの AccountController.Login がこれを 1 回呼び、結果を HTTP (404 / 429 / 401 / 200)・監査ログ・Cookie の発行に写す
    /// (HTTP・監査・Cookie はホストが持つ。<see cref="ExternalLoginService"/> と対になる)。
    /// <code>
    /// var login = new PasswordLogin(designData, db, cache, config.LoginLockout, config.TotpLogin, config.EmailOtpLogin, message => mail.SendAsync(config.EmailOtpLogin.MailInfraName, message));
    /// var result = await login.VerifyAsync(loginInfo.Id, loginInfo.Password, loginInfo.TwoFactorCode);
    /// </code>
    /// 流れ: 契約の確認 (<see cref="LoginAccountStore"/>) → 総当たり対策で 1 回分を数える (<see cref="LoginLockout.TryStartAttemptAsync"/>) → パスワード照合 →
    /// 二要素認証 (契約に TOTP の列があれば認証アプリ <see cref="TotpLogin"/>、無くて TwoFactorEmail があればメール <see cref="EmailOtpLogin"/>) → 全部通ったら数え直す (<see cref="LoginLockout.ResetAsync"/>)。
    /// 数えておいた 1 回の扱い: パスワード・コードの不一致 = 失敗として残す / コードを求めた・送れなかった・期限切れ = 取り消す (失敗ではない) / 照合中の例外 (DB の障害など) = 取り消して投げる。
    /// 存在しないログイン名は数えない (<see cref="LoginLockout"/> の約束)。
    /// </summary>
    public class PasswordLogin
    {
        readonly DesignData _designData;
        readonly IDbAccessor _db;
        readonly IDistributedCache _cache;
        readonly LoginLockoutSettings _lockout;
        readonly TotpLoginSettings _totp;
        readonly EmailOtpLoginSettings _emailOtp;
        readonly Func<MailMessage, Task<MailSendResult>> _sendMail;

        /// <param name="cache">総当たり対策の回数とメールのコードの置き場。</param>
        /// <param name="sendMail">メールのワンタイムコードの送信 (契約に TwoFactorEmail があるときだけ呼ぶ)。テンプレートは MailDispatcher を通す。</param>
        public PasswordLogin(DesignData designData, IDbAccessor db, IDistributedCache cache,
            LoginLockoutSettings lockout, TotpLoginSettings totp, EmailOtpLoginSettings emailOtp,
            Func<MailMessage, Task<MailSendResult>> sendMail)
        {
            _designData = designData;
            _db = db;
            _cache = cache;
            _lockout = lockout;
            _totp = totp;
            _emailOtp = emailOtp;
            _sendMail = sendMail;
        }

        /// <summary>ログイン名・パスワード・二要素認証のコード (1 段階目は null) を照合する。照合できずに終わった例外 (DB の障害など) はそのまま投げる (失敗には数えない)。</summary>
        public async Task<PasswordLoginResult> VerifyAsync(string? loginName, string? password, string? twoFactorCode)
        {
            var accounts = LoginAccountStore.Create(_designData, _db);
            if (accounts == null || !accounts.HasPassword) return new() { Status = PasswordLoginStatus.NotAvailable };

            var lockout = new LoginLockout(_lockout, _cache);
            var lockId = (await accounts.FindByLoginNameAsync(loginName))?.UserId;
            if (!await lockout.TryStartAttemptAsync(lockId)) return new() { Status = PasswordLoginStatus.Locked, Detail = "Locked", IsDenied = true };

            LoginAccount account;
            try
            {
                var verified = await accounts.VerifyPasswordAsync(loginName, password);
                if (verified == null) return new() { Status = PasswordLoginStatus.Unauthorized };   //数えた 1 回は失敗として残す
                account = verified;

                var totp = TotpLogin.Create(_designData, _totp, _db);
                if (totp != null)
                {
                    var result = await totp.VerifyAsync(account.UserId, account.LoginName, twoFactorCode);
                    if (result.Status != TotpLoginStatus.Ok) return await TwoFactorAsync(lockout, lockId, result, result.Status, result.Status == TotpLoginStatus.InvalidCode);
                }
                else if (accounts.HasTwoFactorEmail)
                {
                    var email = new EmailOtpLogin(_emailOtp, _sendMail, _cache);
                    var result = await email.VerifyAsync(account.UserId, account.TwoFactorEmail ?? string.Empty, twoFactorCode);
                    if (result.Status != EmailOtpLoginStatus.Ok) return await TwoFactorAsync(lockout, lockId, result, result.Status, result.Status == EmailOtpLoginStatus.InvalidCode);
                }
            }
            catch
            {
                //照合できずに終わった回は失敗に数えない。取り消し自体の失敗 (キャッシュの障害) で元の例外を隠さない
                try { await lockout.CancelAttemptAsync(lockId); } catch { }
                throw;
            }

            await lockout.ResetAsync(lockId);
            //クライアントは status だけを見る (形は二要素認証の結果と同じ)
            return new() { Status = PasswordLoginStatus.Ok, Account = account, Response = new TotpLoginResult { Status = TotpLoginStatus.Ok } };
        }

        //二要素認証の途中。コードの不一致は失敗として残す。コードを求めた・送れなかった・期限切れは失敗ではないので取り消す
        static async Task<PasswordLoginResult> TwoFactorAsync(LoginLockout lockout, string? lockId, object response, string status, bool isInvalidCode)
        {
            if (!isInvalidCode) await lockout.CancelAttemptAsync(lockId);
            return new() { Status = PasswordLoginStatus.TwoFactor, Response = response, Detail = $"TwoFactor={status}", IsDenied = isInvalidCode };
        }
    }
}
