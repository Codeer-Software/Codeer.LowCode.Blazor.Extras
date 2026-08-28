using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Codeer.Mail.Gmail
{
    /// <summary>Gmail の 1 日の送信上限に達した (その日はリトライしても送れない)。</summary>
    public class GmailDailyQuotaExceededException : InvalidOperationException
    {
        public GmailDailyQuotaExceededException(string message) : base(message) { }
    }

    /// <summary>
    /// Gmail API (users.messages.send) の素の REST 呼び出し。SDK なし。
    /// レート制限 (429) / 一時的なサービス不可 (503) は指数バックオフで再試行し (Retry-After があればそれに従う)、
    /// 1 日の送信上限は <see cref="GmailDailyQuotaExceededException"/> で呼び出し側に打ち切らせる。
    /// 上限の目安: Workspace = 1 ユーザー 1 日 2,000 通・無料 Gmail = 500 通、約 2.5 通/秒 (250 quota units/秒、send = 100 units)。
    /// </summary>
    public class GmailApiClient
    {
        static readonly HttpClient _sharedClient = new();
        public const string SendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";

        //再試行回数と指数バックオフの初期値 (2s → 4s → 8s → 16s → 32s)
        public const int MaxRetryCount = 5;
        static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

        /// <summary>連続送信の最短間隔 (レート上限に張り付かせず 429 を避ける)。<see cref="WaitForNextSendAsync"/> が使う。</summary>
        public TimeSpan MinSendInterval { get; set; } = TimeSpan.FromMilliseconds(400);

        /// <summary>待機の差し替え口 (テストで実時間を待たないため)。</summary>
        public Func<TimeSpan, Task> DelayAsync { get; set; } = Task.Delay;

        readonly HttpClient _http;
        readonly System.Diagnostics.Stopwatch _sinceLastSend = new();

        public GmailApiClient(HttpClient? httpClient = null)
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

        /// <summary>1 通送る。<paramref name="accessTokenProvider"/> は再試行のたびに呼ばれる (期限切れの更新を任せる)。失敗は例外。</summary>
        public async Task SendAsync(Func<Task<string>> accessTokenProvider, GmailMessage message)
            => await SendRawAsync(accessTokenProvider, await GmailMimeBuilder.CreateRawAsync(message));

        /// <summary>MIME バイト列をそのまま送る。</summary>
        public async Task SendRawAsync(Func<Task<string>> accessTokenProvider, byte[] rawMime)
        {
            //Gmail は MIME 全体を base64url の "raw" で受け取る。この JSON エンドポイントは数MB 上限のため、
            //それを超える添付は別のアップロードエンドポイントが必要になる。
            var payload = JsonSerializer.Serialize(new { raw = Base64Url(rawMime) });
            for (var retry = 0; ; retry++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessTokenProvider());
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode) return;

                if ((response.StatusCode == (HttpStatusCode)429 || response.StatusCode == HttpStatusCode.ServiceUnavailable) && retry < MaxRetryCount)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? RetryBaseDelay * Math.Pow(2, retry);
                    await DelayAsync(wait);
                    continue;
                }
                var body = await response.Content.ReadAsStringAsync();
                var error = $"Gmail send failed ({(int)response.StatusCode}): {body}";
                //1 日の送信上限 (Workspace 2,000 通 / 無料 500 通) はリトライしても回復しない
                if (IsDailyQuotaExceeded(body)) throw new GmailDailyQuotaExceededException(error);
                throw new InvalidOperationException(error);
            }
        }

        public static bool IsDailyQuotaExceeded(string body)
            => body.Contains("Daily user sending quota exceeded", StringComparison.OrdinalIgnoreCase)
               || body.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase)
               || body.Contains("5.4.5", StringComparison.Ordinal);

        public static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
