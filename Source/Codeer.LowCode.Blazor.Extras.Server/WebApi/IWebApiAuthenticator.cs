namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    /// <summary>
    /// WebApiField の接続先の認証。送信を包む形で、送る前にヘッダー・クエリを付ける / 署名を計算する / 401 なら取り直して送り直す、をこの中で書く。
    /// 標準の方式 (None / ApiKey / Bearer / Basic / OAuth2ClientCredentials) は WebApiApi が持っている。独自の方式は実装して WebApiApi のコンストラクタに渡す (同じ名前なら標準より優先)。
    /// </summary>
    public interface IWebApiAuthenticator
    {
        /// <summary>appsettings の接続先の Authentication に書く名前 (大文字小文字は区別しない)。</summary>
        string Name { get; }

        /// <summary>
        /// request に認証を付けて next で送る。next は request の写しを送るので、request を直して何度でも渡せる (送り直し)。
        /// 方式の設定は connection.GetAuthenticationSettings&lt;T&gt;() で読む。トークンの取得など別の URL への要求も next で送れる。
        /// 例外を投げると呼び出しは StatusCode 0 で終わる (画面に例外の文言は出さない)。
        /// cancellationToken は接続先のタイムアウト (next で送る要求には掛かっている)。応答の本文を読む・待つなど、この中で待つ処理に渡す。
        /// </summary>
        Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken);
    }
}
