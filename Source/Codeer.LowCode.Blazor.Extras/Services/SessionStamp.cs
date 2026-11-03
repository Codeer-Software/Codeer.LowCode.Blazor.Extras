using System.Security.Cryptography;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    /// <summary>
    /// セッションの印 (LoginAccountContractField の DbColumnSessionStamp に入れる値)。ユーザー行に 1 つ持ち、ログインで発行する Cookie に同じ値を積む。
    /// 新しい値に書き換えるとそれまでの Cookie は全部照合に落ちる (= ログイン済みセッションの失効)。秘密ではないがサーバーだけが作る。
    /// </summary>
    public static class SessionStamp
    {
        /// <summary>新しい印 (32 バイトの乱数の base64url)。毎回違う値になる。</summary>
        public static string Create()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
