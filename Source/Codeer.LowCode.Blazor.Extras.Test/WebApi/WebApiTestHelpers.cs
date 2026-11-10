using Codeer.LowCode.Blazor.Extras.Server.WebApi;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.WebApi
{
    /// <summary>送られた要求を記録して、Responder の応答を返す偽のハンドラ。</summary>
    class RecordingHandler : HttpMessageHandler
    {
        public record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body, string? ContentType);

        public List<Sent> Requests { get; } = new();

        public Func<Sent, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; }
            = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(e => e.Key, e => string.Join(",", e.Value), StringComparer.OrdinalIgnoreCase);
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var sent = new Sent(request.Method, request.RequestUri!, headers, body, request.Content?.Headers.ContentType?.ToString());
            lock (Requests) Requests.Add(sent);
            return await Responder(sent, cancellationToken);
        }

        public static HttpResponseMessage Json(HttpStatusCode code, string json)
            => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    /// <summary>時計を進められる TimeProvider (OAuth2 のトークンの期限)。</summary>
    class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    static class WebApiTestHelpers
    {
        //appsettings の "WebApi" セクションと同じ形の JSON から読む
        public static WebApiSettings LoadSettings(string json)
        {
            var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
            return WebApiSettings.Load(configuration.GetSection("WebApi"));
        }

        //接続先 1 つ ("Api"。BaseUrl https://api.example.com/v1/) の設定。authentication は接続先の中に足す JSON ("Authentication": ..., "ApiKey": {...})
        public static WebApiSettings OneConnection(string authentication = "", int timeoutSeconds = 30, string baseUrl = "https://api.example.com/v1/")
            => LoadSettings($$"""
            {
              "WebApi": {
                "Connections": [
                  { "Name": "Api", "BaseUrl": "{{baseUrl}}", "TimeoutSeconds": {{timeoutSeconds}}{{(string.IsNullOrEmpty(authentication) ? "" : ", " + authentication)}} }
                ]
              }
            }
            """);
    }
}
