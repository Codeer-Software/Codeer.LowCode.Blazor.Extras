using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>HTTPプロバイダ(SendGrid/Graph)のリクエスト形状検証用のフェイク。</summary>
    class FakeHttpHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        public Func<HttpRequestMessage, string, HttpResponseMessage> Responder { get; set; }
            = (_, _) => new HttpResponseMessage(HttpStatusCode.Accepted);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return Responder(request, body);
        }

        public HttpClient CreateClient() => new(this);
    }
}
