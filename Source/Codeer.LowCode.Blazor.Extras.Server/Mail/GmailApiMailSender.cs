using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Gmail API (users.messages.send) implementation of <see cref="IMailSender"/>.
    /// Service account + domain-wide delegation (Google Workspace), plain REST (no SDK) -
    /// sends as the <see cref="MailInfraSettings.SenderMailAddress"/> user.
    /// Settings: SenderMailAddress = the delegated sender user,
    /// ClientSecret = the service account JSON key (file path, or the JSON text itself).
    /// Suited for notification mails (Workspace sending limits are around 2000 mails/day);
    /// use a delivery service for large bulk sends.
    /// </summary>
    public class GmailApiMailSender : IMailSender
    {
        static readonly HttpClient _sharedClient = new();
        const int MaxRetryCount = 3;

        readonly MailInfraSettings _settings;
        readonly HttpClient _http;
        //委任ユーザー (sub) ごとのトークンキャッシュ (動的 From はそのユーザーとして送るため)
        readonly Dictionary<string, (string Token, DateTime ExpiresAtUtc)> _tokens = new();

        public GmailApiMailSender(MailInfraSettings settings, HttpClient? httpClient = null)
        {
            _settings = settings;
            _http = httpClient ?? _sharedClient;
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
            //Gmail takes the full MIME as base64url "raw". This JSON endpoint is limited to a few MB -
            //larger attachments would need the separate upload endpoint.
            var payload = JsonSerializer.Serialize(new { raw = Base64Url(await CreateRawMimeAsync(message)) });
            for (var retry = 0; ; retry++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
                //動的 From はそのユーザーとして送る (ドメイン全体の委任が対象ユーザーを含むこと。許可ドメインは MailDispatcher が検証済み)
                var sendAsUser = string.IsNullOrEmpty(message.From) ? _settings.SenderMailAddress : message.From;
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetTokenAsync(sendAsUser));
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode) return;

                //rate limited - honor Retry-After
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
            if (_tokens.TryGetValue(sendAsUser, out var cached) && DateTime.UtcNow < cached.ExpiresAtUtc) return cached.Token;

            var response = await _http.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = CreateAssertion(sendAsUser),
                }));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gmail token request failed ({(int)response.StatusCode}): {text}");

            using var json = JsonDocument.Parse(text);
            var token = json.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
            _tokens[sendAsUser] = (token, DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60)));
            return token;
        }

        //JWT (RS256) signed with the service account private key. sub = the delegated user to send as.
        string CreateAssertion(string sendAsUser)
        {
            var (clientEmail, privateKeyPem) = LoadServiceAccountKey();
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

        (string ClientEmail, string PrivateKeyPem) LoadServiceAccountKey()
        {
            var text = _settings.ClientSecret.TrimStart().StartsWith('{')
                ? _settings.ClientSecret
                : File.ReadAllText(_settings.ClientSecret);
            using var json = JsonDocument.Parse(text);
            return (json.RootElement.GetProperty("client_email").GetString()!,
                json.RootElement.GetProperty("private_key").GetString()!);
        }

        static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
