using System.Net;
using System.Text;
using System.Web;

namespace Codeer.Mail.Gmail
{
    /// <summary>
    /// 同意フローで、Google からのリダイレクト (ループバック) を 1 回だけ受ける。
    /// デスクトップ種別: ポートは空いているものを自動で選ぶ (Google はループバックの任意ポートを許可する)。
    /// Web 種別: 事前登録したリダイレクト URI と完全一致が必要なので、固定 URI を指定する。
    /// </summary>
    public sealed class LoopbackCodeReceiver : IDisposable
    {
        readonly HttpListener _listener = new();

        public string RedirectUri { get; }

        /// <param name="fixedRedirectUri">Web 種別クライアント用の固定 URI (例 http://localhost:53682/)。null なら空きポートを自動で選ぶ。</param>
        public LoopbackCodeReceiver(string? fixedRedirectUri = null)
        {
            //HttpListener の prefix は末尾 / が必要 (Google に登録する値も末尾 / 付きで揃える)
            RedirectUri = string.IsNullOrEmpty(fixedRedirectUri)
                ? $"http://127.0.0.1:{GetFreePort()}/"
                : fixedRedirectUri.EndsWith('/') ? fixedRedirectUri : fixedRedirectUri + "/";
            _listener.Prefixes.Add(RedirectUri);
            _listener.Start();
        }

        /// <summary>認可コードを待つ。state が一致しないときは例外。ユーザーがキャンセルしたときは null。</summary>
        public async Task<string?> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => { try { _listener.Stop(); } catch { } });
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
            var code = query["code"];
            var error = query["error"];
            var state = query["state"];

            var ok = string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code) && state == expectedState;
            await WriteResponseAsync(context.Response, ok);

            if (!string.IsNullOrEmpty(error)) return error == "access_denied" ? null : throw new InvalidOperationException($"Google returned an error: {error}");
            if (state != expectedState) throw new InvalidOperationException("The state of the redirect does not match (possible CSRF).");
            return code;
        }

        static async Task WriteResponseAsync(HttpListenerResponse response, bool ok)
        {
            var html = ok
                ? "<html><body style='font-family:sans-serif'><h3>認証が完了しました。このウィンドウを閉じてアプリに戻ってください。</h3></body></html>"
                : "<html><body style='font-family:sans-serif'><h3>認証は完了しませんでした。アプリに戻ってやり直してください。</h3></body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }

        static int GetFreePort()
        {
            var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            return port;
        }

        public void Dispose()
        {
            try { _listener.Close(); } catch { }
        }
    }
}
