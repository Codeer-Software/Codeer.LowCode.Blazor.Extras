using Codeer.LowCode.Blazor.Extras.Server.WebApi;
using Codeer.LowCode.Blazor.Extras.WebApi;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.WebApi
{
    /// <summary>
    /// WebApiClient (実行エンジン): メソッドごとの URL / クエリ / ボディ、パスの差し込みとエスケープ、BaseUrl の外へ出る試み、
    /// 相手の 4xx / 5xx・タイムアウト・通信の失敗・応答の上限・リダイレクト。送信は偽のハンドラで受ける。
    /// </summary>
    public class WebApiClientTest
    {
        static (WebApiClient Client, RecordingHandler Handler) Create(WebApiSettings? settings = null)
        {
            var handler = new RecordingHandler();
            return (new WebApiClient(settings ?? WebApiTestHelpers.OneConnection(), [], handler), handler);
        }

        [Test]
        public async Task Getは差し込みに使わなかった最上位のスカラー値をクエリにしボディは無い()
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "users/{id}/orders",
                """{"Id":5,"Status":"open now","Limit":10,"Active":true,"Note":null,"Filter":{"a":1},"Tags":[1,2]}""", null);

            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.ErrorMessage, Is.Empty);
            var sent = handler.Requests.Single();
            Assert.That(sent.Method, Is.EqualTo(HttpMethod.Get));
            //キーは渡した綴りのまま (差し込みの {id} は Id とも合う)。null・オブジェクト・配列は載せない
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users/5/orders?Status=open%20now&Limit=10&Active=true"));
            Assert.That(sent.Body, Is.Empty);
        }

        [Test]
        public async Task Deleteもクエリに載せボディは無い()
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Delete, "users/{id}", """{"id":"a1","force":true}""", null);
            var sent = handler.Requests.Single();
            Assert.That(sent.Method, Is.EqualTo(HttpMethod.Delete));
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users/a1?force=true"));
            Assert.That(sent.Body, Is.Empty);
        }

        [TestCase(WebApiMethod.Post, "POST")]
        [TestCase(WebApiMethod.Put, "PUT")]
        public async Task PostとPutは引数の全体をJSONのボディにしクエリは付けない(WebApiMethod method, string httpMethod)
        {
            var (client, handler) = Create();
            var args = """{"id":7,"name":"山田","items":[1,2]}""";
            await client.ExecuteAsync("Api", method, "users/{id}", args, null);
            var sent = handler.Requests.Single();
            Assert.That(sent.Method.Method, Is.EqualTo(httpMethod));
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users/7"));
            Assert.That(sent.Body, Is.EqualTo(args));
            Assert.That(sent.ContentType, Does.StartWith("application/json"));
        }

        [Test]
        public async Task 引数が空ならオブジェクトが空と同じ()
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Post, "items", "", null);
            Assert.That(handler.Requests.Single().Body, Is.EqualTo("{}"));
        }

        [Test]
        public async Task キーの名前は変えずにボディに載せる()
        {
            var (client, handler) = Create();
            var args = """{"Id":1,"userName":"山田","User_name":"a","Address":{"ZipCode":"100","city":{"Name":"x"}},"items":[{"ItemCode":"A"},[{"deep":true}]],"Note":null}""";
            await client.ExecuteAsync("Api", WebApiMethod.Put, "orders", args, null);
            Assert.That(handler.Requests.Single().Body, Is.EqualTo(args));
        }

        [Test]
        public async Task キーの名前は変えずにクエリに載せる()
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Get, "search", """{"PageSize":10,"q":"a","Active":true}""", null);
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/search?PageSize=10&q=a&Active=true"));
        }

        [TestCase("{id}", """{"Id":5}""")]
        [TestCase("{Id}", """{"id":5}""")]
        [TestCase("{id}", """{"id":5}""")]
        [TestCase("{Id}", """{"Id":5}""")]
        [TestCase("{userId}", """{"UserId":5}""")]
        public async Task 差し込みの名前は先頭1文字の大文字小文字を区別しない(string placeholder, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, $"users/{placeholder}", args, null);
            Assert.That(ret.ErrorMessage, Is.Empty);
            //差し込みに使ったキーはクエリに載せない
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users/5"));
        }

        [TestCase("{userid}", """{"userId":5}""")]
        [TestCase("{USERID}", """{"UserId":5}""")]
        public async Task 差し込みの名前の2文字目以降は大文字小文字を区別する(string placeholder, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, $"users/{placeholder}", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("{id}", """{"Id":1,"id":2}""", "https://api.example.com/v1/users/2?Id=1")]
        [TestCase("{id}", """{"id":2,"Id":1}""", "https://api.example.com/v1/users/2?Id=1")]
        [TestCase("{Id}", """{"id":2,"Id":1}""", "https://api.example.com/v1/users/1?id=2")]
        [TestCase("{Id}", """{"Id":1,"id":2}""", "https://api.example.com/v1/users/1?id=2")]
        public async Task 先頭1文字だけ違うキーが両方あれば差し込みは完全に一致するほう(string placeholder, string args, string expected)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, $"users/{placeholder}", args, null);
            Assert.That(ret.ErrorMessage, Is.Empty);
            //使わなかったほうはそのままクエリに載る
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo(expected));
        }

        [Test]
        public async Task 先頭1文字だけ違うキーは両方そのまま送る()
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Post, "x", """{"Id":1,"id":2,"name":"a"}""", null);
            Assert.That(handler.Requests.Single().Body, Is.EqualTo("""{"Id":1,"id":2,"name":"a"}"""));

            await client.ExecuteAsync("Api", WebApiMethod.Get, "x", """{"id":1,"Id":2}""", null);
            Assert.That(handler.Requests[1].Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/x?id=1&Id=2"));
        }

        [Test]
        public async Task 差し込みの値はエスケープする()
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Get, "files/{name}", """{"name":"a b/c?d#e&f=g%h"}""", null);
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/files/a%20b%2Fc%3Fd%23e%26f%3Dg%25h"));
        }

        [TestCase("""{}""")]
        [TestCase("""{"id":null}""")]
        [TestCase("""{"id":""}""")]
        [TestCase("""{"id":{"a":1}}""")]
        [TestCase("""{"id":[1]}""")]
        public async Task 差し込みの値が無ければ送らない(string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "users/{id}", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("パスの差し込み 'id' の値がありません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("users/{id")]
        [TestCase("users/id}")]
        [TestCase("users/{}")]
        [TestCase("users/{a{b}}")]
        public async Task パスの書き方が壊れていれば送らない(string path)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, path, """{"id":1,"a":1,"b":1}""", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("WebApiField のパスの書き方が正しくありません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("""{"id":".."}""")]
        [TestCase("""{"id":"."}""")]
        public async Task 差し込みの値でドットの区間を作れない(string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "users/{id}/orders", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(handler.Requests, Is.Empty);
        }

        //差し込みの値で . / .. の区間を作る試み (相手のサーバーが %2F・%5C・%2E を復号してから正規化すると上の階層に届く)。送らない
        [TestCase("users/{v}", """{"v":"../../admin/keys"}""")]
        [TestCase("users/{v}", """{"v":"..\\..\\x"}""")]
        [TestCase("users/{v}", """{"v":"a/../b"}""")]
        [TestCase("users/{v}", """{"v":"a/./b"}""")]
        [TestCase("users/{v}", """{"v":"../.."}""")]
        [TestCase("users/{v}", """{"v":"..\\.."}""")]
        [TestCase("users/{v}", """{"v":"%2e%2e/x"}""")]
        [TestCase("users/{v}", """{"v":"%2e%2e"}""")]
        [TestCase("users/a/{v}", """{"v":"%2F..%2F"}""")]
        //二重・三重に符号化したもの
        [TestCase("users/{v}", """{"v":"%252e%252e/x"}""")]
        [TestCase("users/{v}", """{"v":"%252e%252e%252fx"}""")]
        [TestCase("users/{v}", """{"v":"a%252F%25252e%25252e%252Fb"}""")]
        public async Task 差し込みの値で上の階層に出る区間は送らない(string path, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, path, args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("組み立てた URL が接続先の BaseUrl の外を指しています。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        //"/" を含むだけの値・"." を含む普通の値は 1 区間のまま送る
        [TestCase("a/b", "https://api.example.com/v1/users/a%2Fb")]
        [TestCase("v1.2", "https://api.example.com/v1/users/v1.2")]
        [TestCase("file.txt", "https://api.example.com/v1/users/file.txt")]
        [TestCase("..x", "https://api.example.com/v1/users/..x")]
        [TestCase("x..", "https://api.example.com/v1/users/x..")]
        public async Task ドットやスラッシュを含む普通の値は送る(string value, string expected)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "users/{v}", JsonSerializer.Serialize(new { v = value }), null);
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo(expected));
        }

        //デザインのパスで BaseUrl の外へ出る試み。送らない
        [TestCase("../admin")]
        [TestCase("a/../../admin")]
        [TestCase("%2e%2e/admin")]
        [TestCase("a/%2E%2E/%2e%2e/admin")]
        public async Task BaseUrlの外を指すパスは送らない(string path)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, path, "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("組み立てた URL が接続先の BaseUrl の外を指しています。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        //別のホストを指そうとしても BaseUrl の下のパスになる (送るなら必ず BaseUrl の配下)
        [TestCase("https://evil.example.com/x", "{}")]
        [TestCase("//evil.example.com/x", "{}")]
        [TestCase("@evil.example.com/x", "{}")]
        [TestCase("/x", "{}")]
        [TestCase("{v}", """{"v":"https://evil.example.com/x"}""")]
        [TestCase("{v}", """{"v":"//evil.example.com"}""")]
        [TestCase("{v}", """{"v":"@evil.example.com"}""")]
        public async Task 送るときは必ずBaseUrlの配下(string path, string args)
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Get, path, args, null);
            foreach (var sent in handler.Requests)
            {
                Assert.That(sent.Uri.Host, Is.EqualTo("api.example.com"));
                Assert.That(sent.Uri.Scheme, Is.EqualTo("https"));
                Assert.That(sent.Uri.Port, Is.EqualTo(443));
                Assert.That(sent.Uri.AbsolutePath, Does.StartWith("/v1/"));
                Assert.That(sent.Uri.AbsolutePath.Split('/').Select(Uri.UnescapeDataString), Has.None.EqualTo(".."));
            }
        }

        [Test]
        public async Task BaseUrlにパスが無くても末尾のスラッシュが無くても配下を判定する()
        {
            var (client, handler) = Create(WebApiTestHelpers.OneConnection(baseUrl: "https://api.example.com/v1"));
            await client.ExecuteAsync("Api", WebApiMethod.Get, "users", "{}", null);
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users"));

            //v1 の兄弟 (v10) には出られない
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "../v10/users", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("ftp://api.example.com/")]
        [TestCase("api.example.com/v1/")]
        [TestCase("https://user:pass@api.example.com/")]
        [TestCase("https://api.example.com/?key=1")]
        public async Task BaseUrlが正しくなければ送らない(string baseUrl)
        {
            var (client, handler) = Create(WebApiTestHelpers.OneConnection(baseUrl: baseUrl));
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先の BaseUrl が正しくありません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task パスに書いたクエリにはスカラー値を続けて足す()
        {
            var (client, handler) = Create();
            await client.ExecuteAsync("Api", WebApiMethod.Get, "search?q={q}&lang=ja", """{"q":"a&b","page":2}""", null);
            Assert.That(handler.Requests.Single().Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/search?q=a%26b&lang=ja&page=2"));
        }

        // ===== 外側がオブジェクトでない引数 (配列・値だけ) =====

        [TestCase(WebApiMethod.Post, "POST", """[{"id":1,"name":"a"},{"id":2,"name":"b"}]""")]
        [TestCase(WebApiMethod.Put, "PUT", """[{"id":1,"name":"a"},{"id":2,"name":"b"}]""")]
        [TestCase(WebApiMethod.Post, "POST", "[]")]
        [TestCase(WebApiMethod.Post, "POST", "\"abc\"")]
        [TestCase(WebApiMethod.Put, "PUT", "123")]
        [TestCase(WebApiMethod.Post, "POST", "true")]
        public async Task PostとPutはオブジェクトでない引数をそのままボディにする(WebApiMethod method, string httpMethod, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", method, "items", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.ErrorMessage, Is.Empty);
            var sent = handler.Requests.Single();
            Assert.That(sent.Method.Method, Is.EqualTo(httpMethod));
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/items"));
            Assert.That(sent.Body, Is.EqualTo(args));
            Assert.That(sent.ContentType, Does.StartWith("application/json"));
        }

        [TestCase(WebApiMethod.Get, """[{"id":1},{"id":2}]""")]
        [TestCase(WebApiMethod.Delete, """[{"id":1},{"id":2}]""")]
        [TestCase(WebApiMethod.Get, "\"abc\"")]
        [TestCase(WebApiMethod.Delete, "123")]
        [TestCase(WebApiMethod.Get, "null")]
        public async Task GetとDeleteはオブジェクトでない引数からクエリを付けずボディも無い(WebApiMethod method, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", method, "search?lang=ja", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.ErrorMessage, Is.Empty);
            var sent = handler.Requests.Single();
            //パスに書いたクエリだけ
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/search?lang=ja"));
            Assert.That(sent.Body, Is.Empty);
        }

        [TestCase(WebApiMethod.Get, """[{"id":1}]""")]
        [TestCase(WebApiMethod.Post, """[{"id":1}]""")]
        [TestCase(WebApiMethod.Put, "\"abc\"")]
        [TestCase(WebApiMethod.Delete, "123")]
        public async Task オブジェクトでない引数で差し込みのあるパスは送らない(WebApiMethod method, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", method, "users/{id}", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("パスの差し込み 'id' の値がありません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [TestCase("../admin", """[{"id":1}]""")]
        [TestCase("%2e%2e/admin", "\"abc\"")]
        public async Task オブジェクトでない引数でもBaseUrlの外を指すパスは送らない(string path, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Post, path, args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("組み立てた URL が接続先の BaseUrl の外を指しています。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        //JSON として読めない引数は送らない (文言は以前のまま)
        [TestCase(WebApiMethod.Post, "not json")]
        [TestCase(WebApiMethod.Get, "{\"a\":")]
        public async Task 引数がJSONとして読めなければ送らない(WebApiMethod method, string args)
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Api", method, "x", args, null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("引数が JSON として読めません。"));
            Assert.That(handler.Requests, Is.Empty);
        }

        [Test]
        public async Task 接続先が設定に無ければStatusCode0()
        {
            var (client, handler) = Create();
            var ret = await client.ExecuteAsync("Nope", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Nope' が設定にありません。"));
            Assert.That(handler.Requests, Is.Empty);

            //名前の大文字小文字は区別しない
            Assert.That((await client.ExecuteAsync("api", WebApiMethod.Get, "x", "{}", null)).StatusCode, Is.EqualTo(200));
        }

        [TestCase(HttpStatusCode.BadRequest)]
        [TestCase(HttpStatusCode.NotFound)]
        [TestCase(HttpStatusCode.InternalServerError)]
        [TestCase(HttpStatusCode.ServiceUnavailable)]
        public async Task 相手の4xxと5xxはそのまま返す(HttpStatusCode code)
        {
            var (client, handler) = Create();
            handler.Responder = (_, _) => Task.FromResult(RecordingHandler.Json(code, """{"error":"bad"}"""));
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo((int)code));
            Assert.That(ret.Body, Is.EqualTo("""{"error":"bad"}"""));
            Assert.That(ret.ErrorMessage, Is.Empty);
        }

        [Test]
        public async Task リダイレクトは追わずにそのまま返す()
        {
            Assert.That(WebApiClient.CreateDefaultHandler().AllowAutoRedirect, Is.False);

            var (client, handler) = Create();
            handler.Responder = (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://evil.example.com/");
                return Task.FromResult(response);
            };
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(302));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task タイムアウトはStatusCode0()
        {
            var (client, handler) = Create(WebApiTestHelpers.OneConnection(timeoutSeconds: 1));
            handler.Responder = async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            };
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' から時間内に応答がありませんでした。"));
        }

        [Test]
        public async Task 通信の例外はStatusCode0で例外の文言を出さない()
        {
            var (client, handler) = Create(WebApiTestHelpers.OneConnection("""
                "Authentication": "ApiKey", "ApiKey": { "QueryName": "key", "Key": "SECRET-KEY-123" }
                """));
            handler.Responder = (sent, _) => throw new HttpRequestException($"failed {sent.Uri}");
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' に接続できませんでした。"));
            Assert.That(ret.ErrorMessage, Does.Not.Contain("SECRET"));

            handler.Responder = (sent, _) => throw new InvalidOperationException($"boom {sent.Uri}");
            ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の呼び出しに失敗しました。"));
            Assert.That(ret.ErrorMessage, Does.Not.Contain("SECRET"));
        }

        [Test]
        public async Task 応答の上限を超えたらStatusCode0()
        {
            var (client, handler) = Create();
            //長さの分からない (チャンク) 応答
            handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new UnknownLengthStream(WebApiClient.MaxResponseBytes + 1)),
            });
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Api' の応答が大きすぎます。"));

            //Content-Length で分かる応答
            handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[WebApiClient.MaxResponseBytes + 1]) });
            ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(0));

            //上限ちょうどは読める
            handler.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new UnknownLengthStream(WebApiClient.MaxResponseBytes)),
            });
            ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.Body, Has.Length.EqualTo(WebApiClient.MaxResponseBytes));
        }

        [Test]
        public async Task 応答の文字コードはcharsetに従う()
        {
            var (client, handler) = Create();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("shift_jis");
            handler.Responder = (_, _) =>
            {
                var content = new ByteArrayContent(sjis.GetBytes("""{"name":"山田"}"""));
                content.Headers.ContentType = new("application/json") { CharSet = "shift_jis" };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            };
            var ret = await client.ExecuteAsync("Api", WebApiMethod.Get, "x", "{}", null);
            Assert.That(ret.Body, Is.EqualTo("""{"name":"山田"}"""));
        }

        //Length を持たない (= Content-Length が付かない) 'a' の並び
        sealed class UnknownLengthStream(long length) : Stream
        {
            long _position;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                var n = (int)Math.Min(count, length - _position);
                for (var i = 0; i < n; i++) buffer[offset + i] = (byte)'a';
                _position += n;
                return n;
            }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
