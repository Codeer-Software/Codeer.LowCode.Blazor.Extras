using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// <see cref="IMailSender"/> の Microsoft Graph (sendMail) 実装。
    /// クライアントクレデンシャル + 素の REST (SDK なし)。組織のメールボックスから送る
    /// 通知メール向き。Exchange Online のレート制限があるため大量の一斉送信には不向き。
    /// </summary>
    public class GraphApiMailSender : IMailSender
    {
        static readonly HttpClient _sharedClient = new();
        const int MaxRetryCount = 3;

        readonly GraphApiSettings _settings;
        readonly HttpClient _http;
        string? _token;
        DateTime _tokenExpiresAtUtc;

        public GraphApiMailSender(GraphApiSettings settings, HttpClient? httpClient = null)
        {
            _settings = settings;
            _http = httpClient ?? _sharedClient;
        }

        public int MaxBulkCount => _settings.MaxBulkCount;

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
            for (var retry = 0; ; retry++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(GetSendAsUser(message))}/sendMail");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetTokenAsync());
                request.Content = new StringContent(CreateSendMailPayload(message).ToJsonString(), Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.Accepted) return;

                //スロットリング時は Retry-After に従って再試行
                if (response.StatusCode == (HttpStatusCode)429 && retry < MaxRetryCount)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    await Task.Delay(wait);
                    continue;
                }
                throw new InvalidOperationException($"Graph sendMail failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
            }
        }

        //動的 From はそのユーザーのメールボックスから送る (アプリケーション権限 Mail.Send はテナント内の全ユーザーで送れる。
        //許可ドメインは MailDispatcher が検証済み)。送信済みアイテムも本人に残る
        string GetSendAsUser(MailMessage message)
            => string.IsNullOrEmpty(message.From) ? _settings.SenderMailAddress : message.From;

        JsonObject CreateSendMailPayload(MailMessage message)
        {
            static JsonObject Address(string address) => new() { ["emailAddress"] = new JsonObject { ["address"] = address } };
            static JsonArray Addresses(IEnumerable<string> addresses) => new(addresses.Select(e => (JsonNode)Address(e)).ToArray());

            var graphMessage = new JsonObject
            {
                ["subject"] = message.Subject,
                ["body"] = new JsonObject
                {
                    ["contentType"] = message.IsBodyHtml ? "HTML" : "Text",
                    ["content"] = message.Body,
                },
                ["toRecipients"] = Addresses(message.To),
            };
            if (message.Cc.Any()) graphMessage["ccRecipients"] = Addresses(message.Cc);
            if (message.Bcc.Any()) graphMessage["bccRecipients"] = Addresses(message.Bcc);
            if (!string.IsNullOrEmpty(message.ReplyTo)) graphMessage["replyTo"] = Addresses(new[] { message.ReplyTo });
            if (message.Attachments.Any())
            {
                graphMessage["attachments"] = new JsonArray(message.Attachments.Select(e => (JsonNode)new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = e.FileName,
                    ["contentBytes"] = e.ContentBase64,
                }).ToArray());
            }
            if (message.Headers.Any())
            {
                //Graph が許すカスタムヘッダは "x-"/"X-" 始まりのみ
                graphMessage["internetMessageHeaders"] = new JsonArray(message.Headers
                    .Where(e => e.Key.StartsWith("x-", StringComparison.OrdinalIgnoreCase))
                    .Select(e => (JsonNode)new JsonObject { ["name"] = e.Key, ["value"] = e.Value }).ToArray());
            }

            return new JsonObject
            {
                ["message"] = graphMessage,
                ["saveToSentItems"] = true,
            };
        }

        async Task<string> GetTokenAsync()
        {
            if (_token != null && DateTime.UtcNow < _tokenExpiresAtUtc) return _token;

            var response = await _http.PostAsync(
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(_settings.TenantId)}/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _settings.ClientId,
                    ["client_secret"] = _settings.ClientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials",
                }));
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph token request failed ({(int)response.StatusCode}): {text}");

            using var json = JsonDocument.Parse(text);
            _token = json.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = json.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 300;
            _tokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return _token;
        }
    }
}
