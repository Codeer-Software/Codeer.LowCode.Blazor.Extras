using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codeer.Mail.Gmail
{
    /// <summary>トークン交換の結果。</summary>
    public class GmailTokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public int ExpiresInSeconds { get; set; }
        /// <summary>openid email スコープを含めたときに id_token から取れる、同意したアカウントのアドレス。</summary>
        public string? Email { get; set; }
    }

    /// <summary>PKCE の code_verifier / code_challenge (S256)。</summary>
    public record GmailPkce(string CodeVerifier, string CodeChallenge)
    {
        public static GmailPkce Create()
        {
            var verifier = GmailApiClient.Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = GmailApiClient.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            return new GmailPkce(verifier, challenge);
        }
    }

    /// <summary>
    /// Google OAuth 2.0 (認可コードフロー) の素の REST。同意画面 URL・コード交換・リフレッシュ・取り消し。
    /// デスクトップアプリ種別のクライアントは client_secret を持たず PKCE を使う
    /// (Google の仕様上、リフレッシュ時の client_secret は Optional。リフレッシュトークン + client_id で更新できる)。
    /// </summary>
    public class GmailOAuth
    {
        static readonly HttpClient _sharedClient = new();
        public const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        public const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
        public const string SendScope = "https://www.googleapis.com/auth/gmail.send";
        /// <summary>送信 + 同意したアカウントの確認 (id_token の email)。</summary>
        public const string SendWithEmailScope = SendScope + " openid email";

        readonly HttpClient _http;

        public GmailOAuth(HttpClient? httpClient = null)
        {
            _http = httpClient ?? _sharedClient;
        }

        /// <summary>
        /// 同意画面の URL。access_type=offline + prompt=consent でリフレッシュトークンを毎回もらう
        /// (2 回目以降の同意で refresh_token が省かれるのを防ぐ)。
        /// </summary>
        public static string CreateAuthorizationUrl(string clientId, string redirectUri, string scope, string state,
            GmailPkce? pkce = null, string? loginHint = null)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = scope,
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["include_granted_scopes"] = "true",
                ["state"] = state,
            };
            if (pkce != null)
            {
                query["code_challenge"] = pkce.CodeChallenge;
                query["code_challenge_method"] = "S256";
            }
            if (!string.IsNullOrEmpty(loginHint)) query["login_hint"] = loginHint;
            return AuthorizationEndpoint + "?" + string.Join("&", query.Select(e => $"{e.Key}={Uri.EscapeDataString(e.Value)}"));
        }

        /// <summary>認可コードをトークンに交換する。client_secret は web 種別のときだけ。</summary>
        public async Task<GmailTokenResponse> ExchangeCodeAsync(string clientId, string? clientSecret, string code, string redirectUri, GmailPkce? pkce = null)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
            };
            if (!string.IsNullOrEmpty(clientSecret)) form["client_secret"] = clientSecret;
            if (pkce != null) form["code_verifier"] = pkce.CodeVerifier;
            return await PostTokenAsync(form);
        }

        /// <summary>リフレッシュトークンからアクセストークンを得る。</summary>
        public async Task<GmailTokenResponse> RefreshAsync(string clientId, string? clientSecret, string refreshToken)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
            };
            if (!string.IsNullOrEmpty(clientSecret)) form["client_secret"] = clientSecret;
            return await PostTokenAsync(form);
        }

        /// <summary>サービスアカウントの JWT (jwt-bearer) をアクセストークンに交換する (ドメイン全体の委任モード)。</summary>
        public async Task<GmailTokenResponse> ExchangeJwtAsync(string assertion)
            => await PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            });

        /// <summary>トークン (リフレッシュ / アクセス) を Google 側で取り消す。失敗は例外。</summary>
        public async Task RevokeAsync(string token)
        {
            var response = await _http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }));
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gmail token revoke failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        async Task<GmailTokenResponse> PostTokenAsync(Dictionary<string, string> form)
        {
            var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gmail token request failed ({(int)response.StatusCode}): {text}");

            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            return new GmailTokenResponse
            {
                AccessToken = root.GetProperty("access_token").GetString()!,
                RefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                ExpiresInSeconds = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300,
                Email = root.TryGetProperty("id_token", out var i) ? GetEmailFromIdToken(i.GetString()) : null,
            };
        }

        /// <summary>サービスアカウントの秘密鍵で署名した JWT (RS256)。sub = 成り代わって送る委任ユーザー。</summary>
        public static string CreateServiceAccountAssertion(string clientEmail, string privateKeyPem, string subject, string scope = SendScope)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = GmailApiClient.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = GmailApiClient.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = clientEmail,
                sub = subject,
                scope,
                aud = TokenEndpoint,
                iat = now,
                exp = now + 3600,
            }));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signature = GmailApiClient.Base64Url(rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return $"{header}.{claims}.{signature}";
        }

        /// <summary>値 (JSON {"refresh_token":"..."} かトークン文字列そのもの) からリフレッシュトークンを取り出す。</summary>
        public static string? ParseRefreshToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (!value.StartsWith('{')) return value;
            try
            {
                using var json = JsonDocument.Parse(value);
                return json.RootElement.TryGetProperty("refresh_token", out var token) && token.GetString() is { Length: > 0 } t ? t : null;
            }
            catch
            {
                return null;
            }
        }

        //id_token (Google から TLS で直接受け取った JWT) の payload から email を読む。署名検証は不要 (発行元から直接受け取っている)
        public static string? GetEmailFromIdToken(string? idToken)
        {
            if (string.IsNullOrEmpty(idToken)) return null;
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                var bytes = Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '='));
                using var json = JsonDocument.Parse(bytes);
                return json.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
