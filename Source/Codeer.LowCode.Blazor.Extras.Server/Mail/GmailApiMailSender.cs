using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// <see cref="IMailSender"/> の Gmail API (users.messages.send) 実装。素の REST (SDK なし)。
    /// <see cref="MailInfraSettings.ClientSecret"/> の JSON の種類で認証モードが決まる:
    ///
    /// ① サービスアカウントキー (client_email/private_key) = ドメイン全体の委任 (Google Workspace・管理者設定が必要)。
    ///    <see cref="MailInfraSettings.SenderMailAddress"/> のユーザーとして送信し、動的 From はそのユーザーに成り代わる。
    /// ② OAuth クライアント (installed/web) = ユーザー同意モード (管理者不要)。
    ///    本人がブラウザで 1 回同意して得たリフレッシュトークン (<see cref="MailInfraSettings.TokenSecret"/>) で、
    ///    **同意したユーザー本人として**送信する。営業担当者などが自分のアドレスで送るための経路。
    ///    動的 From はそのアカウントの Gmail 側で送信者エイリアス (Send As) が設定されている場合のみ有効
    ///    (未設定なら Gmail が本人アドレスに書き換える)。
    ///
    /// 通知メール向き (Workspace の送信上限は 2000通/日 程度)。大量の一斉送信は配信サービスを使うこと。
    /// </summary>
    public class GmailApiMailSender : IMailSender
    {
        static readonly HttpClient _sharedClient = new();
        const int MaxRetryCount = 3;
        //ユーザー同意モードのトークンキャッシュキー (委任モードは委任ユーザーごと)
        const string OAuthUserCacheKey = "(oauth-user)";

        readonly MailInfraSettings _settings;
        readonly HttpClient _http;
        //差出人アドレス→ユーザー単位のリフレッシュトークン (ユーザー同意モード。null/未解決 = システムトークンで送る)。
        //実装は MailUserStore.FindRefreshTokenAsync (差出人でユーザーモジュールを検索) をテンプレの MailController が結線する
        readonly Func<string, Task<string?>>? _userRefreshTokenResolver;
        //委任ユーザー (sub) ごとのトークンキャッシュ (動的 From はそのユーザーとして送るため)
        readonly Dictionary<string, (string Token, DateTime ExpiresAtUtc)> _tokens = new();

        public GmailApiMailSender(MailInfraSettings settings, HttpClient? httpClient = null,
            Func<string, Task<string?>>? userRefreshTokenResolver = null)
        {
            _settings = settings;
            _http = httpClient ?? _sharedClient;
            _userRefreshTokenResolver = userRefreshTokenResolver;
        }

        public async Task<MailSendResult> SendAsync(MailMessage message)
        {
            try
            {
                await SendCoreAsync(message);
                return MailSendResult.Success(1);
            }
            catch (Exception ex)
            {
                return MailSendResult.Failure(string.Join(";", message.To), ex.Message);
            }
        }

        public async Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
        {
            var result = new MailSendResult { TotalCount = recipients.Count };
            foreach (var recipient in recipients)
            {
                try
                {
                    await SendCoreAsync(SmtpMailSender.CreateResolvedMessage(template, recipient));
                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.Failures.Add(new MailSendFailure { To = recipient.To, Error = ex.Message });
                }
            }
            return result;
        }

        async Task SendCoreAsync(MailMessage message)
        {
            //Gmail は MIME 全体を base64url の "raw" で受け取る。この JSON エンドポイントは数MB 上限のため、
            //それを超える添付は別のアップロードエンドポイントが必要になる。
            var payload = JsonSerializer.Serialize(new { raw = Base64Url(await CreateRawMimeAsync(message)) });
            for (var retry = 0; ; retry++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
                //動的 From はそのユーザーとして送る (委任モードのみ。ドメイン全体の委任が対象ユーザーを含むこと。許可ドメインは MailDispatcher が検証済み)
                var sendAsUser = string.IsNullOrEmpty(message.From) ? _settings.SenderMailAddress : message.From;
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetTokenAsync(sendAsUser));
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode) return;

                //レート制限時は Retry-After に従って再試行
                if (response.StatusCode == (HttpStatusCode)429 && retry < MaxRetryCount)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    await Task.Delay(wait);
                    continue;
                }
                throw new InvalidOperationException($"Gmail send failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
            }
        }

        async Task<byte[]> CreateRawMimeAsync(MailMessage message)
        {
            var mime = SmtpMailSender.CreateMimeMessage(_settings, message);
            using var stream = new MemoryStream();
            await mime.WriteToAsync(stream);
            return stream.ToArray();
        }

        async Task<string> GetTokenAsync(string sendAsUser)
        {
            var key = LoadKey();

            //委任モード = サービスアカウント鍵で署名した JWT を交換 (sub = 委任ユーザー)
            if (key.IsServiceAccount)
                return await GetOrExchangeAsync(sendAsUser, () => new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = CreateAssertion(key.ClientEmail, key.PrivateKeyPem, sendAsUser),
                });

            //ユーザー同意モード: 差出人にユーザー単位トークンが登録されていれば「その人として」送る
            //(本人の送信済みに残る)。無ければシステムのトークン (TokenSecret) で送る
            if (_tokens.TryGetValue(sendAsUser, out var cached) && DateTime.UtcNow < cached.ExpiresAtUtc) return cached.Token;
            var userToken = _userRefreshTokenResolver == null || string.IsNullOrEmpty(sendAsUser)
                ? null
                : ParseRefreshToken(await _userRefreshTokenResolver(sendAsUser));
            if (userToken != null)
                return await GetOrExchangeAsync(sendAsUser, () => CreateRefreshTokenForm(key, userToken));
            return await GetOrExchangeAsync(OAuthUserCacheKey, () => CreateRefreshTokenForm(key, LoadRefreshToken()));
        }

        static Dictionary<string, string> CreateRefreshTokenForm(GmailKey key, string refreshToken) => new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = key.ClientId,
            ["client_secret"] = key.ClientSecret,
            ["refresh_token"] = refreshToken,
        };

        async Task<string> GetOrExchangeAsync(string cacheKey, Func<Dictionary<string, string>> createForm)
        {
            if (_tokens.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow < cached.ExpiresAtUtc) return cached.Token;

            var response = await _http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(createForm()));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gmail token request failed ({(int)response.StatusCode}): {text}");

            using var json = JsonDocument.Parse(text);
            var token = json.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
            _tokens[cacheKey] = (token, DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }

        //ユーザー行のトークン列の値 (JSON {"refresh_token":"..."} かトークン文字列そのもの) からリフレッシュトークンを取り出す
        static string? ParseRefreshToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (!value.StartsWith('{')) return value;
            try
            {
                using var json = JsonDocument.Parse(value);
                return json.RootElement.TryGetProperty("refresh_token", out var token) && token.GetString() is { Length: > 0 } t
                    ? t : null;
            }
            catch
            {
                return null;
            }
        }

        //サービスアカウントの秘密鍵で署名した JWT (RS256)。sub = 成り代わって送る委任ユーザー。
        string CreateAssertion(string clientEmail, string privateKeyPem, string sendAsUser)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var claims = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = clientEmail,
                sub = sendAsUser,
                scope = "https://www.googleapis.com/auth/gmail.send",
                aud = "https://oauth2.googleapis.com/token",
                iat = now,
                exp = now + 3600,
            }));
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signature = Base64Url(rsa.SignData(Encoding.ASCII.GetBytes($"{header}.{claims}"),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return $"{header}.{claims}.{signature}";
        }

        record GmailKey(bool IsServiceAccount, string ClientEmail, string PrivateKeyPem, string ClientId, string ClientSecret);

        //ClientSecret の JSON の種類で認証モードを判定する
        GmailKey LoadKey()
        {
            using var json = JsonDocument.Parse(ReadPathOrJson(_settings.ClientSecret));
            var root = json.RootElement;

            //サービスアカウントキー (ドメイン全体の委任モード)
            if (root.TryGetProperty("client_email", out var email) && root.TryGetProperty("private_key", out var privateKey))
                return new(true, email.GetString()!, privateKey.GetString()!, string.Empty, string.Empty);

            //OAuth クライアント (ユーザー同意モード)。installed (デスクトップ) / web どちらの形式も受ける
            if (root.TryGetProperty("installed", out var app) || root.TryGetProperty("web", out app))
                return new(false, string.Empty, string.Empty,
                    app.GetProperty("client_id").GetString()!, app.GetProperty("client_secret").GetString()!);

            throw new InvalidOperationException(
                "Gmail ClientSecret is neither a service account key (client_email/private_key) nor an OAuth client secret (installed/web).");
        }

        string LoadRefreshToken()
        {
            if (string.IsNullOrEmpty(_settings.TokenSecret))
                throw new InvalidOperationException(
                    "Gmail TokenSecret is not configured. The OAuth client mode needs a refresh token JSON ({\"refresh_token\":\"...\"}) obtained by the user's one-time consent.");

            using var json = JsonDocument.Parse(ReadPathOrJson(_settings.TokenSecret));
            if (json.RootElement.TryGetProperty("refresh_token", out var token) && token.GetString() is { Length: > 0 } value)
                return value;
            throw new InvalidOperationException("Gmail TokenSecret JSON does not contain \"refresh_token\".");
        }

        static string ReadPathOrJson(string value)
            => value.TrimStart().StartsWith('{') ? value : File.ReadAllText(value);

        static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
