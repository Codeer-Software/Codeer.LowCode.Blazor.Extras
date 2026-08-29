using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codeer.Mail.Graph
{
    /// <summary>
    /// Microsoft Graph の sendMail (本人 = /me) の素の REST 呼び出し。SDK なし。
    /// スロットリング (429) / 一時的なサービス不可 (503) は Retry-After に従って再試行する。
    /// 送ったメールは本人の「送信済みアイテム」に残る (saveToSentItems)。
    /// Exchange Online の上限の目安: 1 分あたり 30 通、1 日あたり 10,000 宛先。
    /// </summary>
    public class GraphApiClient
    {
        static readonly HttpClient _sharedClient = new();
        public const string SendEndpoint = "https://graph.microsoft.com/v1.0/me/sendMail";
        public const string MeEndpoint = "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName,displayName";

        public const int MaxRetryCount = 5;
        static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

        /// <summary>連続送信の最短間隔 (30 通/分の上限に張り付かせない)。<see cref="WaitForNextSendAsync"/> が使う。</summary>
        public TimeSpan MinSendInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>待機の差し替え口 (テストで実時間を待たないため)。</summary>
        public Func<TimeSpan, Task> DelayAsync { get; set; } = Task.Delay;

        readonly HttpClient _http;
        readonly System.Diagnostics.Stopwatch _sinceLastSend = new();

        public GraphApiClient(HttpClient? httpClient = null)
        {
            _http = httpClient ?? _sharedClient;
        }

        /// <summary>前の送信から <see cref="MinSendInterval"/> 空ける (一斉送信のループの先頭で呼ぶ)。</summary>
        public async Task WaitForNextSendAsync()
        {
            if (_sinceLastSend.IsRunning)
            {
                var remaining = MinSendInterval - _sinceLastSend.Elapsed;
                if (remaining > TimeSpan.Zero) await DelayAsync(remaining);
            }
            _sinceLastSend.Restart();
        }

        /// <summary>本人のメールアドレス (mail が無ければ UPN) と表示名。</summary>
        public async Task<(string Email, string DisplayName)> GetMeAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, MeEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Graph /me failed ({(int)response.StatusCode}): {text}");
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            var mail = root.TryGetProperty("mail", out var m) ? m.GetString() : null;
            var upn = root.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
            var name = root.TryGetProperty("displayName", out var d) ? d.GetString() : null;
            return ((string.IsNullOrEmpty(mail) ? upn : mail) ?? string.Empty, name ?? string.Empty);
        }

        /// <summary>1 通送る。<paramref name="accessTokenProvider"/> は再試行のたびに呼ばれる (期限切れの更新を任せる)。失敗は例外。</summary>
        public async Task SendAsync(Func<Task<string>> accessTokenProvider, MailMessage message)
        {
            var payload = CreateSendMailPayload(message).ToJsonString();
            for (var retry = 0; ; retry++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessTokenProvider());
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.Accepted) return;

                if ((response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.ServiceUnavailable) && retry < MaxRetryCount)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? RetryBaseDelay * Math.Pow(2, retry);
                    await DelayAsync(wait);
                    continue;
                }
                var body = await response.Content.ReadAsStringAsync();
                var error = $"Graph sendMail failed ({(int)response.StatusCode}): {ExtractMessage(body)}";
                //認証・権限の失敗は残りを送っても同じ結果
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new MailSendAbortException(error);
                throw new InvalidOperationException(error);
            }
        }

        /// <summary>Graph の sendMail の JSON。From は付けない (本人名義。Send As を使うときだけ from を入れる)。</summary>
        public static JsonObject CreateSendMailPayload(MailMessage message)
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
            if (!string.IsNullOrEmpty(message.From))
            {
                var from = new JsonObject { ["address"] = message.From };
                if (!string.IsNullOrEmpty(message.FromDisplayName)) from["name"] = message.FromDisplayName;
                graphMessage["from"] = new JsonObject { ["emailAddress"] = from };
            }
            if (message.Cc.Count > 0) graphMessage["ccRecipients"] = Addresses(message.Cc);
            if (message.Bcc.Count > 0) graphMessage["bccRecipients"] = Addresses(message.Bcc);
            if (!string.IsNullOrEmpty(message.ReplyTo)) graphMessage["replyTo"] = Addresses(new[] { message.ReplyTo });
            if (message.Attachments.Count > 0)
            {
                graphMessage["attachments"] = new JsonArray(message.Attachments.Select(e => (JsonNode)new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = e.FileName,
                    ["contentBytes"] = e.ContentBase64,
                }).ToArray());
            }
            if (message.Headers.Count > 0)
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

        static string ExtractMessage(string body)
        {
            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } s) return s;
            }
            catch { }
            return body;
        }
    }
}
