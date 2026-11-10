using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Extras.WebApi;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    /// <summary>
    /// 接続先の設定で外部の Web API を呼ぶ。失敗で例外を投げない: 相手の 4xx / 5xx はそのまま返し、呼び出せなかったときは StatusCode 0 + ErrorMessage。
    /// HttpClient は 1 つを使い回す。リダイレクトは追わない。
    /// 引数の JSON は受け取ったまま (キーの名前を変えずに) クエリ・ボディ・パスの差し込みに使う。外側がオブジェクトでない引数はボディにだけ使う。
    /// </summary>
    sealed class WebApiClient
    {
        internal const int MaxResponseBytes = 10 * 1024 * 1024;

        readonly HttpClient _http;
        readonly Dictionary<string, WebApiConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, IWebApiAuthenticator> _authenticators = new(StringComparer.OrdinalIgnoreCase);

        //handler はテスト用 (無ければリダイレクトを追わない既定のハンドラ)
        internal WebApiClient(WebApiSettings settings, IEnumerable<IWebApiAuthenticator> authenticators, HttpMessageHandler? handler = null, TimeProvider? time = null)
        {
            _http = new HttpClient(handler ?? CreateDefaultHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            foreach (var e in settings.Connections) _connections[e.Name] = e;
            IWebApiAuthenticator[] standards =
            [
                new NoneAuthenticator(),
                new ApiKeyAuthenticator(),
                new BearerAuthenticator(),
                new BasicAuthenticator(),
                new OAuth2ClientCredentialsAuthenticator(time ?? TimeProvider.System),
            ];
            foreach (var e in standards) _authenticators[e.Name] = e;
            //ホストの独自の方式 (同じ名前なら標準より優先)
            foreach (var e in authenticators) _authenticators[e.Name] = e;
        }

        internal static SocketsHttpHandler CreateDefaultHandler() => new()
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        internal async Task<WebApiExecuteResponse> ExecuteAsync(string connectionName, WebApiMethod method, string path, string arguments, ILogger? logger)
        {
            if (!_connections.TryGetValue(connectionName, out var connection)) return Error(string.Format(Resources.WebApi_ConnectionNotFound, connectionName));
            var authenticationName = string.IsNullOrEmpty(connection.Authentication) ? "None" : connection.Authentication;
            if (!_authenticators.TryGetValue(authenticationName, out var authenticator))
            {
                return Error(string.Format(Resources.WebApi_UnknownAuthentication, connection.Name, authenticationName));
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(connection.TimeoutSeconds > 0 ? connection.TimeoutSeconds : WebApiConnection.DefaultTimeoutSeconds));
            try
            {
                if (string.IsNullOrWhiteSpace(arguments)) arguments = "{}";
                //外側がオブジェクトでない引数 (配列・値だけ) も送る: Post / Put はそのままボディ、Get / Delete はクエリなし、パスの差し込みは値が無いのでエラー
                JsonDocument json;
                try
                {
                    json = JsonDocument.Parse(arguments);
                }
                catch (JsonException)
                {
                    return Error(Resources.WebApi_ArgumentsNotObject);
                }
                using (json)
                {
                    var hasBody = method is WebApiMethod.Post or WebApiMethod.Put;
                    var uri = WebApiUrlBuilder.Build(connection.BaseUrl, path, json.RootElement, appendQuery: !hasBody);
                    using var request = new HttpRequestMessage(ToHttpMethod(method), uri);
                    if (hasBody)
                    {
                        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(arguments));
                        request.Content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
                    }

                    using var response = await authenticator.SendAsync(request, connection, async e => await _http.SendAsync(await CopyAsync(e), HttpCompletionOption.ResponseHeadersRead, timeout.Token), timeout.Token);
                    return new WebApiExecuteResponse
                    {
                        StatusCode = (int)response.StatusCode,
                        Body = await ReadBodyAsync(response, connection, timeout.Token),
                    };
                }
            }
            catch (WebApiCallException e)
            {
                return Error(e.Message);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return Error(string.Format(Resources.WebApi_Timeout, connection.Name));
            }
            catch (HttpRequestException e)
            {
                logger?.LogWarning(e, "WebApi: could not connect to '{Connection}'.", connection.Name);
                return Error(string.Format(Resources.WebApi_ConnectFailed, connection.Name));
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "WebApi: the call to '{Connection}' failed.", connection.Name);
                return Error(string.Format(Resources.WebApi_CallFailed, connection.Name));
            }
        }

        static WebApiExecuteResponse Error(string message) => new() { StatusCode = 0, ErrorMessage = message };

        static HttpMethod ToHttpMethod(WebApiMethod method) => method switch
        {
            WebApiMethod.Post => HttpMethod.Post,
            WebApiMethod.Put => HttpMethod.Put,
            WebApiMethod.Delete => HttpMethod.Delete,
            _ => HttpMethod.Get,
        };

        //HttpRequestMessage は 1 回しか送れないので、送るたびに写しを作る (認証の送り直しのため)
        static async Task<HttpRequestMessage> CopyAsync(HttpRequestMessage source)
        {
            var copy = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version, VersionPolicy = source.VersionPolicy };
            foreach (var e in source.Headers) copy.Headers.TryAddWithoutValidation(e.Key, e.Value);
            if (source.Content != null)
            {
                copy.Content = new ByteArrayContent(await source.Content.ReadAsByteArrayAsync());
                foreach (var e in source.Content.Headers) copy.Content.Headers.TryAddWithoutValidation(e.Key, e.Value);
            }
            return copy;
        }

        //上限 (MaxResponseBytes) を超えたら WebApiCallException (応答が大きすぎる)。OAuth2 のトークン応答もこれで読む
        internal static async Task<string> ReadBodyAsync(HttpResponseMessage response, WebApiConnection connection, CancellationToken cancellationToken)
        {
            var tooLarge = new WebApiCallException(string.Format(Resources.WebApi_ResponseTooLarge, connection.Name));
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw tooLarge;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) throw tooLarge;
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            var encoding = Encoding.UTF8;
            var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            if (!string.IsNullOrEmpty(charset))
            {
                try { encoding = Encoding.GetEncoding(charset); } catch (ArgumentException) { }
            }
            using var reader = new StreamReader(buffer, encoding, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }
}
