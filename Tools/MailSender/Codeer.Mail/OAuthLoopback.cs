using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace Codeer.Mail
{
    /// <summary>base64url (パディング無し)。</summary>
    public static class Base64Url
    {
        public static string Encode(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '='));
        }
    }

    /// <summary>PKCE の code_verifier / code_challenge (S256)。</summary>
    public record Pkce(string CodeVerifier, string CodeChallenge)
    {
        public static Pkce Create()
        {
            var verifier = Base64Url.Encode(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            return new Pkce(verifier, challenge);
        }
    }

    /// <summary>OAuth の id_token (発行元から TLS で直接受け取った JWT) の payload を読む。署名検証は不要 (発行元から直接受け取っている)。</summary>
    public static class IdToken
    {
        /// <summary>最初に見つかったクレームの値 (複数指定は優先順)。</summary>
        public static string? GetClaim(string? idToken, params string[] names)
        {
            if (string.IsNullOrEmpty(idToken)) return null;
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(Base64Url.Decode(parts[1]));
                foreach (var name in names)
                {
                    if (json.RootElement.TryGetProperty(name, out var e) && e.GetString() is { Length: > 0 } v) return v;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 同意フローで、認可サーバーからのリダイレクト (ループバック) を 1 回だけ受ける。
    /// 既定はポートを空いているものから自動で選ぶ (Google / Microsoft ともループバックの任意ポートを許可する)。
    /// 事前登録した URI と完全一致が必要なとき (Google の Web 種別) は固定 URI を指定する。
    /// </summary>
    public sealed class LoopbackCodeReceiver : IDisposable
    {
        readonly HttpListener _listener = new();

        public string RedirectUri { get; }

        /// <param name="fixedRedirectUri">固定 URI (例 http://localhost:53682/)。null なら空きポートを自動で選ぶ。</param>
        /// <param name="host">自動選択時のホスト名。Google は 127.0.0.1、Microsoft は localhost (登録値 http://localhost と一致させる)。</param>
        public LoopbackCodeReceiver(string? fixedRedirectUri = null, string host = "127.0.0.1")
        {
            //HttpListener の prefix は末尾 / が必要 (登録する値も末尾 / 付きで揃える)
            RedirectUri = string.IsNullOrEmpty(fixedRedirectUri)
                ? $"http://{host}:{GetFreePort()}/"
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
            var errorDescription = query["error_description"];
            var state = query["state"];

            var ok = string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(code) && state == expectedState;
            await WriteResponseAsync(context.Response, ok);

            if (!string.IsNullOrEmpty(error))
                return error == "access_denied" ? null : throw new InvalidOperationException($"The authorization server returned an error: {error} {errorDescription}".Trim());
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
