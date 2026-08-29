using System.Text.Json;

namespace Codeer.Mail.Graph
{
    /// <summary>トークン交換の結果。</summary>
    public class GraphTokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        /// <summary>Microsoft はリフレッシュのたびに新しいリフレッシュトークンを返すことがある (返ってきたら差し替えて保存する)。</summary>
        public string? RefreshToken { get; set; }
        public int ExpiresInSeconds { get; set; }
        /// <summary>id_token の preferred_username (通常は本人のメールアドレス = UPN)。</summary>
        public string? UserName { get; set; }
    }

    /// <summary>
    /// Microsoft Entra ID (v2.0 エンドポイント) の認可コードフロー + PKCE。パブリック クライアント (デスクトップ) なのでシークレットは使わない。
    /// Entra のアプリ登録は「モバイル アプリケーションとデスクトップ アプリケーション」プラットフォームに http://localhost を登録し、
    /// 委任されたアクセス許可 Mail.Send (+ User.Read) を持たせる。
    /// </summary>
    public class GraphOAuth
    {
        static readonly HttpClient _sharedClient = new();

        /// <summary>職場または学校アカウント (任意のテナント)。個人の Microsoft アカウントも受けるなら "common"。</summary>
        public const string DefaultTenant = "organizations";

        /// <summary>送信 (本人名義) + 本人のアドレス確認 + リフレッシュトークン。</summary>
        public const string SendScope = "https://graph.microsoft.com/Mail.Send https://graph.microsoft.com/User.Read offline_access openid profile email";

        readonly HttpClient _http;

        public GraphOAuth(HttpClient? httpClient = null)
        {
            _http = httpClient ?? _sharedClient;
        }

        static string Authority(string tenant) => $"https://login.microsoftonline.com/{Uri.EscapeDataString(string.IsNullOrEmpty(tenant) ? DefaultTenant : tenant)}/oauth2/v2.0";

        /// <summary>同意画面の URL。</summary>
        /// <param name="selectAccount">true ならアカウント選択画面を出す (別アカウントの追加用)。</param>
        public static string CreateAuthorizationUrl(string tenant, string clientId, string redirectUri, string state, Pkce pkce,
            string? loginHint = null, bool selectAccount = false)
        {
            var query = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["response_mode"] = "query",
                ["scope"] = SendScope,
                ["state"] = state,
                ["code_challenge"] = pkce.CodeChallenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = selectAccount ? "select_account" : "login",
            };
            if (!string.IsNullOrEmpty(loginHint)) query["login_hint"] = loginHint;
            return Authority(tenant) + "/authorize?" + string.Join("&", query.Select(e => $"{e.Key}={Uri.EscapeDataString(e.Value)}"));
        }

        /// <summary>認可コードをトークンに交換する。</summary>
        public async Task<GraphTokenResponse> ExchangeCodeAsync(string tenant, string clientId, string code, string redirectUri, Pkce pkce)
            => await PostTokenAsync(tenant, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = pkce.CodeVerifier,
                ["scope"] = SendScope,
            });

        /// <summary>リフレッシュトークンからアクセストークンを得る。</summary>
        public async Task<GraphTokenResponse> RefreshAsync(string tenant, string clientId, string refreshToken)
            => await PostTokenAsync(tenant, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = clientId,
                ["refresh_token"] = refreshToken,
                ["scope"] = SendScope,
            });

        async Task<GraphTokenResponse> PostTokenAsync(string tenant, Dictionary<string, string> form)
        {
            var response = await _http.PostAsync(Authority(tenant) + "/token", new FormUrlEncodedContent(form));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Microsoft token request failed ({(int)response.StatusCode}): {ExtractErrorDescription(text)}");

            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            return new GraphTokenResponse
            {
                AccessToken = root.GetProperty("access_token").GetString()!,
                RefreshToken = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
                ExpiresInSeconds = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300,
                UserName = root.TryGetProperty("id_token", out var i) ? IdToken.GetClaim(i.GetString(), "preferred_username", "email", "upn") : null,
            };
        }

        //Entra のエラー JSON は長い (トレース ID 等)。人が読む部分だけにする
        static string ExtractErrorDescription(string text)
        {
            try
            {
                using var json = JsonDocument.Parse(text);
                if (json.RootElement.TryGetProperty("error_description", out var d) && d.GetString() is { Length: > 0 } s) return s;
            }
            catch { }
            return text;
        }
    }
}
