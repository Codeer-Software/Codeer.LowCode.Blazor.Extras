using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Microsoft Graph (sendMail) implementation of <see cref="IMailSender"/>.
    /// Uses client credentials + plain REST (no SDK). Suited for notification mails
    /// sent from an organization mailbox; Exchange Online rate limits make it
    /// unsuitable for large bulk sends.
    /// </summary>
    public class GraphApiMailSender : IMailSender
    {
        static readonly HttpClient _sharedClient = new();
        const int MaxRetryCount = 3;

        readonly MailSenderSettings _settings;
        readonly HttpClient _http;
        string? _token;
        DateTime _tokenExpiresAtUtc;

        public GraphApiMailSender(MailSenderSettings settings, HttpClient? httpClient = null)
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
            for (var retry = 0; ; retry++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(_settings.SenderMailAddress)}/sendMail");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await GetTokenAsync());
                request.Content = new StringContent(CreateSendMailPayload(message).ToJsonString(), Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.Accepted) return;

                //throttled - honor Retry-After
                if (response.StatusCode == (HttpStatusCode)429 && retry < MaxRetryCount)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    await Task.Delay(wait);
                    continue;
                }
                throw new InvalidOperationException($"Graph sendMail failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
            }
        }

        internal JsonObject CreateSendMailPayload(MailMessage message)
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
                //Graph only allows custom headers starting with "x-"/"X-"
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
