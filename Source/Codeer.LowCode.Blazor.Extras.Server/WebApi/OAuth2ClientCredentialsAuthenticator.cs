using Codeer.LowCode.Blazor.Extras.Server.Properties;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    //接続先の "OAuth2ClientCredentials": { "TokenUrl": "...", "ClientId": "...", "ClientSecret": "...", "Scope": "..." }
    sealed class OAuth2ClientCredentialsSettings
    {
        public string TokenUrl { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
    }

    /// <summary>
    /// OAuth 2.0 クライアントクレデンシャル。トークンを取得して期限 (expires_in の 1 分前) までキャッシュする (接続先ごと・プロセスの中だけ)。
    /// 401 が返ったら 1 回だけ取り直して送り直す。並行する要求の取得は 1 本にまとめる。
    /// </summary>
    sealed class OAuth2ClientCredentialsAuthenticator(TimeProvider time) : IWebApiAuthenticator
    {
        const int DefaultExpiresInSeconds = 3600;
        static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(1);

        sealed record Token(string AccessToken, DateTimeOffset ExpiresAt);

        readonly ConcurrentDictionary<string, Token> _tokens = new();
        readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public OAuth2ClientCredentialsAuthenticator() : this(TimeProvider.System) { }

        public string Name => "OAuth2ClientCredentials";

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
        {
            var settings = connection.GetAuthenticationSettings<OAuth2ClientCredentialsSettings>();
            if (!Uri.TryCreate(settings.TokenUrl, UriKind.Absolute, out var tokenUrl) || string.IsNullOrEmpty(settings.ClientId))
            {
                throw new WebApiCallException(string.Format(Resources.WebApi_AuthenticationSettingsInvalid, connection.Name));
            }
            var key = string.Join("\n", connection.Name, settings.TokenUrl, settings.ClientId, settings.Scope);

            var token = await GetTokenAsync(key, null, tokenUrl, settings, connection, next, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await next(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

            //失効・取り消しされたトークン。取り直して 1 回だけ送り直す
            response.Dispose();
            token = await GetTokenAsync(key, token, tokenUrl, settings, connection, next, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await next(request);
        }

        //stale = 401 になったトークン。キャッシュがそれのままなら取り直す (別の要求が取り直し済みならそれを使う)
        async Task<string> GetTokenAsync(string key, string? stale, Uri tokenUrl, OAuth2ClientCredentialsSettings settings, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
        {
            if (TryGetCached(key, stale, out var cached)) return cached;
            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            //待っている間に時間切れなら排他を取らずに抜ける (取ったら finally で必ず放す)
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (TryGetCached(key, stale, out cached)) return cached;
                var token = await RequestTokenAsync(tokenUrl, settings, connection, next, cancellationToken);
                _tokens[key] = token;
                return token.AccessToken;
            }
            finally
            {
                gate.Release();
            }
        }

        bool TryGetCached(string key, string? stale, out string accessToken)
        {
            accessToken = string.Empty;
            if (!_tokens.TryGetValue(key, out var token) || token.AccessToken == stale || token.ExpiresAt - ExpirySkew <= time.GetUtcNow()) return false;
            accessToken = token.AccessToken;
            return true;
        }

        async Task<Token> RequestTokenAsync(Uri tokenUrl, OAuth2ClientCredentialsSettings settings, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
        {
            var form = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "client_credentials"),
                new("client_id", settings.ClientId),
                new("client_secret", settings.ClientSecret),
            };
            if (!string.IsNullOrEmpty(settings.Scope)) form.Add(new("scope", settings.Scope));
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl) { Content = new FormUrlEncodedContent(form) };
            using var response = await next(request);
            var failed = new WebApiCallException(string.Format(Resources.WebApi_AuthenticationFailed, connection.Name));
            if (!response.IsSuccessStatusCode) throw failed;
            //本文も接続先のタイムアウトと応答の上限の中で読む (止まる・大きすぎる相手で排他を握り続けない)
            string body;
            try
            {
                body = await WebApiClient.ReadBodyAsync(response, connection, cancellationToken);
            }
            catch (WebApiCallException)
            {
                throw failed;
            }
            try
            {
                using var json = JsonDocument.Parse(body);
                var accessToken = json.RootElement.GetProperty("access_token").GetString();
                if (string.IsNullOrEmpty(accessToken)) throw failed;
                var expiresIn = DefaultExpiresInSeconds;
                if (json.RootElement.TryGetProperty("expires_in", out var e))
                {
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) expiresIn = n;
                    else if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out n)) expiresIn = n;
                }
                return new Token(accessToken, time.GetUtcNow().AddSeconds(expiresIn));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw failed;
            }
        }
    }
}
