using Codeer.LowCode.Blazor.Extras.Server.WebApi;
using Codeer.LowCode.Blazor.Extras.WebApi;
using System.Net;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.WebApi
{
    /// <summary>
    /// 接続先の認証: 標準の方式 (None / ApiKey / Bearer / Basic / OAuth2ClientCredentials) で付くもの、OAuth2 のトークンのキャッシュと 401 での取り直し・トークン応答のタイムアウトと上限、
    /// ホストの独自の方式、知らない方式名。
    /// </summary>
    public class WebApiAuthenticatorTest
    {
        const string OAuth2 = """
            "Authentication": "OAuth2ClientCredentials",
            "OAuth2ClientCredentials": { "TokenUrl": "https://login.example.com/token", "ClientId": "client-1", "ClientSecret": "SECRET-123", "Scope": "api.read" }
            """;

        static (WebApiClient Client, RecordingHandler Handler) Create(string authentication, IWebApiAuthenticator[]? custom = null, TimeProvider? time = null)
        {
            var handler = new RecordingHandler();
            return (new WebApiClient(WebApiTestHelpers.OneConnection(authentication), custom ?? [], handler, time), handler);
        }

        [Test]
        public async Task 認証が空ならNoneで何も付けない()
        {
            var (client, handler) = Create("");
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(handler.Requests.Single().Headers.ContainsKey("Authorization"), Is.False);

            (client, handler) = Create("\"Authentication\": \"None\"");
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(handler.Requests.Single().Headers.ContainsKey("Authorization"), Is.False);
        }

        [Test]
        public async Task ApiKeyはヘッダーに付ける()
        {
            var (client, handler) = Create("""
                "Authentication": "ApiKey", "ApiKey": { "HeaderName": "X-Api-Key", "Key": "k-123" }
                """);
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", """{"a":1}""", null);
            var sent = handler.Requests.Single();
            Assert.That(sent.Headers["X-Api-Key"], Is.EqualTo("k-123"));
            Assert.That(sent.Uri.Query, Is.EqualTo("?a=1"));
        }

        [Test]
        public async Task ApiKeyはQueryNameがあればクエリに付ける()
        {
            var (client, handler) = Create("""
                "Authentication": "ApiKey", "ApiKey": { "QueryName": "api_key", "Key": "k&123" }
                """);
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", """{"a":1}""", null);
            var sent = handler.Requests.Single();
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/x?a=1&api_key=k%26123"));
            Assert.That(sent.Headers.ContainsKey("X-Api-Key"), Is.False);

            await client.ExecuteAsync("Api", WebApiMethod.Post, "x", """{"a":1}""", null);
            Assert.That(handler.Requests[1].Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/x?api_key=k%26123"));
        }

        [Test]
        public async Task Bearerは固定のトークンを付ける()
        {
            var (client, handler) = Create("""
                "Authentication": "Bearer", "Bearer": { "Token": "t-1" }
                """);
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(handler.Requests.Single().Headers["Authorization"], Is.EqualTo("Bearer t-1"));
        }

        [Test]
        public async Task Basicはユーザー名とパスワードを付ける()
        {
            var (client, handler) = Create("""
                "Authentication": "Basic", "Basic": { "UserName": "user", "Password": "p@ss:1" }
                """);
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(handler.Requests.Single().Headers["Authorization"], Is.EqualTo("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("user:p@ss:1"))));
        }

        [TestCase("""
            "Authentication": "ApiKey", "ApiKey": { "HeaderName": "X-Api-Key" }
            """)]
        [TestCase("""
            "Authentication": "ApiKey", "ApiKey": { "Key": "k" }
            """)]
        [TestCase("""
            "Authentication": "Bearer"
            """)]
        [TestCase("""
            "Authentication": "Basic", "Basic": { "Password": "p" }
            """)]
        [TestCase("""
            "Authentication": "OAuth2ClientCredentials", "OAuth2ClientCredentials": { "ClientId": "c" }
            """)]
        public async Task 方式の設定が足りなければ送らない(string authentication)
        {
            var (client, handler) = Create(authentication);
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の認証の設定が足りません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task 知らない方式名はStatusCode0()
        {
            var (client, handler) = Create("\"Authentication\": \"Hmac\"");
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の認証方式 'Hmac' がありません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        //トークンの要求には固定のトークン、それ以外は Authorization を見て、current でなければ 401
        static Func<RecordingHandler.Sent, CancellationToken, Task<HttpResponseMessage>> TokenServer(Func<int, string> issue, Func<string> current, int expiresIn = 3600)
        {
            var issued = 0;
            return (sent, _) =>
            {
                if (sent.Uri.Host == "login.example.com")
                {
                    var token = issue(Interlocked.Increment(ref issued));
                    return Task.FromResult(RecordingHandler.Json(HttpStatusCode.OK, $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}"""));
                }
                var ok = sent.Headers.TryGetValue("Authorization", out var auth) && auth == "Bearer " + current();
                return Task.FromResult(RecordingHandler.Json(ok ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, "{}"));
            };
        }

        [Test]
        public async Task OAuth2はトークンを取得して期限までキャッシュする()
        {
            var time = new ManualTimeProvider();
            var (client, handler) = Create(OAuth2, time: time);
            var current = "t1";
            handler.Responder = TokenServer(n => current = "t" + n, () => current, expiresIn: 600);

            Assert.That((await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null)).StatusCode, Is.EqualTo(200));
            Assert.That((await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null)).StatusCode, Is.EqualTo(200));
            var tokenRequests = handler.Requests.Where(e => e.Uri.Host == "login.example.com").ToList();
            Assert.That(tokenRequests, Has.Count.EqualTo(1));
            Assert.That(tokenRequests[0].Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(tokenRequests[0].Body, Is.EqualTo("grant_type=client_credentials&client_id=client-1&client_secret=SECRET-123&scope=api.read"));
            Assert.That(handler.Requests.Where(e => e.Uri.Host == "api.example.com").Select(e => e.Headers["Authorization"]), Is.All.EqualTo("Bearer t1"));

            //期限の 1 分前を過ぎたら取り直す
            time.Now = time.Now.AddSeconds(540);
            Assert.That((await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null)).StatusCode, Is.EqualTo(200));
            Assert.That(handler.Requests.Count(e => e.Uri.Host == "login.example.com"), Is.EqualTo(2));
            Assert.That(handler.Requests.Last().Headers["Authorization"], Is.EqualTo("Bearer t2"));
        }

        [Test]
        public async Task OAuth2は401なら1回だけ取り直して送り直す()
        {
            var (client, handler) = Create(OAuth2);
            var current = string.Empty;
            handler.Responder = TokenServer(n => "t" + n, () => current);

            //相手が t1 を取り消した (t2 なら通る)
            current = "t2";
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Post, "x", """{"a":1}""", null);
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            var apiRequests = handler.Requests.Where(e => e.Uri.Host == "api.example.com").ToList();
            Assert.That(apiRequests.Select(e => e.Headers["Authorization"]), Is.EqualTo(new[] { "Bearer t1", "Bearer t2" }));
            //送り直しもボディは同じ
            Assert.That(apiRequests.Select(e => e.Body), Is.All.EqualTo("""{"a":1}"""));

            //取り直しても 401 ならそのまま返す (送り直しは 1 回だけ)
            current = "never";
            handler.Requests.Clear();
            ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(401));
            Assert.That(handler.Requests.Count(e => e.Uri.Host == "api.example.com"), Is.EqualTo(2));
            Assert.That(handler.Requests.Count(e => e.Uri.Host == "login.example.com"), Is.EqualTo(1));
        }

        [Test]
        public async Task OAuth2は並行する要求でもトークンの取得は1回()
        {
            var (client, handler) = Create(OAuth2);
            var issued = 0;
            handler.Responder = async (sent, token) =>
            {
                if (sent.Uri.Host == "login.example.com")
                {
                    Interlocked.Increment(ref issued);
                    await Task.Delay(50, token);
                    return RecordingHandler.Json(HttpStatusCode.OK, """{"access_token":"t1","expires_in":3600}""");
                }
                return RecordingHandler.Json(HttpStatusCode.OK, "{}");
            };
            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null)));
            Assert.That(results.Select(e => e.StatusCode), Is.All.EqualTo(200));
            Assert.That(issued, Is.EqualTo(1));
        }

        [Test]
        public async Task OAuth2のトークンを取れなければStatusCode0でキーを出さない()
        {
            var (client, handler) = Create(OAuth2);
            handler.Responder = (sent, _) => Task.FromResult(RecordingHandler.Json(HttpStatusCode.BadRequest, """{"error":"invalid_client SECRET-123"}"""));
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の認証に失敗しました。"));
            Assert.That(ret.ErrorMessage, Does.Not.Contain("SECRET"));
            Assert.That(handler.Requests.Count(e => e.Uri.Host == "api.example.com"), Is.EqualTo(0));

            //access_token の無い応答も
            handler.Responder = (sent, _) => Task.FromResult(RecordingHandler.Json(HttpStatusCode.OK, """{"token_type":"Bearer"}"""));
            ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の認証に失敗しました。"));
        }

        //ヘッダーの後で本文が終わらない (release まで、または読む側のキャンセルまで止まる) ストリーム
        sealed class StalledStream(Task release) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                release.Wait();
                return 0;
            }
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await release.WaitAsync(cancellationToken);
                return 0;
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Test]
        public async Task OAuth2のトークン応答の本文で止まっても接続先のタイムアウトで終わり排他を残さない()
        {
            var handler = new RecordingHandler();
            var client = new WebApiClient(WebApiTestHelpers.OneConnection(OAuth2, timeoutSeconds: 1), [], handler);
            var release = new TaskCompletionSource();
            try
            {
                //トークンのエンドポイントがヘッダー (200) だけ返して本文を送らない
                handler.Responder = (sent, _) => Task.FromResult(sent.Uri.Host == "login.example.com"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream(release.Task)) }
                    : RecordingHandler.Json(HttpStatusCode.OK, "{}"));
                //直らなければ戻らないので、テストの側でも待つ時間を区切る
                var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(ret.StatusCode, Is.EqualTo(0));
                Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' から時間内に応答がありませんでした。"));
                Assert.That(handler.Requests.Count(e => e.Uri.Host == "api.example.com"), Is.EqualTo(0));

                //トークンのエンドポイントが戻れば次の呼び出しは成功する (排他が残っていれば待ち続けてタイムアウトになる)
                handler.Responder = TokenServer(_ => "t1", () => "t1");
                ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(ret.StatusCode, Is.EqualTo(200));
                Assert.That(handler.Requests.Last().Headers["Authorization"], Is.EqualTo("Bearer t1"));
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [Test]
        public async Task OAuth2のトークン応答が上限を超えたら認証の失敗()
        {
            var (client, handler) = Create(OAuth2);
            handler.Responder = (sent, _) => Task.FromResult(sent.Uri.Host == "login.example.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[WebApiClient.MaxResponseBytes + 1]) }
                : RecordingHandler.Json(HttpStatusCode.OK, "{}"));
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の認証に失敗しました。"));
            Assert.That(handler.Requests.Count(e => e.Uri.Host == "api.example.com"), Is.EqualTo(0));
        }

        //署名ヘッダーを付ける独自の方式 (設定は接続先の "Hmac" セクション)
        sealed class HmacSettings
        {
            public string Secret { get; set; } = string.Empty;
        }

        sealed class HmacAuthenticator : IWebApiAuthenticator
        {
            public string Name => "Hmac";

            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
            {
                var settings = connection.GetAuthenticationSettings<HmacSettings>();
                request.Headers.Add("X-Signature", settings.Secret + ":" + request.RequestUri!.AbsolutePath);
                return next(request);
            }
        }

        [Test]
        public async Task ホストの独自の方式を名前で使える()
        {
            var (client, handler) = Create("""
                "Authentication": "hmac", "Hmac": { "Secret": "s1" }
                """, [new HmacAuthenticator()]);
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(handler.Requests.Single().Headers["X-Signature"], Is.EqualTo("s1:/v1/x"));
        }

        sealed class OverrideBearer : IWebApiAuthenticator
        {
            public string Name => "Bearer";

            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
            {
                request.Headers.Add("X-Custom", "1");
                return next(request);
            }
        }

        [Test]
        public async Task 独自の方式は同じ名前の標準より優先する()
        {
            var (client, handler) = Create("\"Authentication\": \"Bearer\"", [new OverrideBearer()]);
            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            var sent = handler.Requests.Single();
            Assert.That(sent.Headers["X-Custom"], Is.EqualTo("1"));
            Assert.That(sent.Headers.ContainsKey("Authorization"), Is.False);
        }

        sealed class ThrowingAuthenticator : IWebApiAuthenticator
        {
            public string Name => "Throw";

            public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
                => throw new InvalidOperationException("SECRET-XYZ");
        }

        [Test]
        public async Task 独自の方式の例外は文言を出さずにStatusCode0()
        {
            var (client, handler) = Create("\"Authentication\": \"Throw\"", [new ThrowingAuthenticator()]);
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の呼び出しに失敗しました。"));
            Assert.That(handler.Requests, Is.Empty);
        }
    }
}
