using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using System.Net;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>IEmbeddingProvider の実装: 設定の必須項目、Ollama の HTTP の形 (偽サーバー)、M.E.AI アダプタ。実 API には接続しない。</summary>
    public class EmbeddingProviderTest
    {
        [Test]
        public void 設定が欠けていれば作れない()
        {
            Assert.That(() => new AzureOpenAIEmbeddingProvider(new AzureOpenAIEmbeddingSettings { EndPoint = "https://x.openai.azure.com/", Key = "k" }), Throws.InvalidOperationException, "Deployment 無し");
            Assert.That(() => new OpenAIEmbeddingProvider(new OpenAIEmbeddingSettings { Model = "m" }), Throws.InvalidOperationException, "Key 無し");
            Assert.That(() => new OllamaEmbeddingProvider(new OllamaEmbeddingSettings { Model = "" }), Throws.InvalidOperationException, "Model 無し");

            var azure = new AzureOpenAIEmbeddingProvider(new AzureOpenAIEmbeddingSettings { EndPoint = "https://x.openai.azure.com/", Key = "k", Deployment = "text-embedding-3-small", Dimensions = 1536 });
            Assert.That(azure.ModelId, Is.EqualTo("text-embedding-3-small"));
            Assert.That(azure.Dimensions, Is.EqualTo(1536));
            var openAI = new OpenAIEmbeddingProvider(new OpenAIEmbeddingSettings { Key = "k", Model = "text-embedding-3-large" });
            Assert.That(openAI.ModelId, Is.EqualTo("text-embedding-3-large"));
            Assert.That(openAI.Dimensions, Is.EqualTo(0), "0 = モデル既定");
        }

        [Test]
        public async Task Ollamaはapi_embedに文章をまとめて送りベクトルを受け取る()
        {
            var requests = new List<(Uri Url, JsonDocument Body)>();
            var handler = new FakeHandler(async (request, ct) =>
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                requests.Add((request.RequestUri!, body));
                var inputs = body.RootElement.GetProperty("input").EnumerateArray().Select(e => e.GetString()!).ToList();
                //各文章に長さを埋めた 3 次元ベクトルを返す
                var vectors = inputs.Select(t => new[] { t.Length, 0.5f, -1f });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent(new { model = "bge-m3", embeddings = vectors }) };
            });
            var provider = new OllamaEmbeddingProvider(new OllamaEmbeddingSettings { BaseUrl = "http://ollama.local:11434/", Model = "bge-m3", Dimensions = 3, BatchSize = 2 }, new HttpClient(handler));
            Assert.That(provider.ModelId, Is.EqualTo("bge-m3"));
            Assert.That(provider.Dimensions, Is.EqualTo(3));

            var vectorsOut = await provider.EmbedAsync(["ab", "cde", "f"]);
            Assert.That(vectorsOut.Count, Is.EqualTo(3));
            Assert.That(vectorsOut.Select(v => v[0]), Is.EqualTo(new float[] { 2, 3, 1 }), "順序どおり");
            Assert.That(requests.Count, Is.EqualTo(2), "BatchSize 2 で 3 件 = 2 リクエスト");
            Assert.That(requests[0].Url.ToString(), Is.EqualTo("http://ollama.local:11434/api/embed"));
            Assert.That(requests[0].Body.RootElement.GetProperty("model").GetString(), Is.EqualTo("bge-m3"));
            Assert.That(requests[0].Body.RootElement.GetProperty("input").GetArrayLength(), Is.EqualTo(2));
            Assert.That(requests[1].Body.RootElement.GetProperty("input").GetArrayLength(), Is.EqualTo(1));
        }

        [Test]
        public void Ollamaの失敗は本文つきの例外()
        {
            var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"model 'nope' not found\"}") }));
            var provider = new OllamaEmbeddingProvider(new OllamaEmbeddingSettings { Model = "nope" }, new HttpClient(handler));
            Assert.That(async () => await provider.EmbedAsync(["a"]), Throws.InvalidOperationException.With.Message.Contain("404").And.Message.Contain("not found"));
        }

        [Test]
        public async Task MEAIの生成器はアダプタでそのまま使える()
        {
            var fake = new FakeEmbeddingProvider();
            var generator = new GeneratorFromProvider(fake);
            var provider = new EmbeddingGeneratorProvider(generator, modelId: "wrapped", dimensions: FakeEmbeddingProvider.Dimensions);
            Assert.That(provider.ModelId, Is.EqualTo("wrapped"));
            Assert.That(provider.Dimensions, Is.EqualTo(128));
            var vectors = await provider.EmbedAsync(["納期", "請求"]);
            Assert.That(vectors.Count, Is.EqualTo(2));
            Assert.That(vectors[0], Is.EqualTo(FakeEmbeddingProvider.Embed("納期")));
        }

        static HttpContent JsonContent(object value) => new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

        sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
        }

        //M.E.AI の IEmbeddingGenerator を FakeEmbeddingProvider で作る (アダプタの往復確認用)
        sealed class GeneratorFromProvider(IEmbeddingProvider provider) : Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>
        {
            public async Task<Microsoft.Extensions.AI.GeneratedEmbeddings<Microsoft.Extensions.AI.Embedding<float>>> GenerateAsync(IEnumerable<string> values, Microsoft.Extensions.AI.EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
            {
                var vectors = await provider.EmbedAsync(values.ToList(), cancellationToken);
                return new(vectors.Select(v => new Microsoft.Extensions.AI.Embedding<float>(v)));
            }
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }

    /// <summary>
    /// 実際の Azure OpenAI に AzureOpenAIEmbeddingProvider で到達する (課金あり・ネットワーク要のため Explicit)。
    /// 環境変数 AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY、デプロイ名は AZURE_OPENAI_EMBEDDING_MODEL (省略時 text-embedding-3-small)。
    /// </summary>
    [Explicit("実 Azure OpenAI を呼ぶ。AZURE_OPENAI_ENDPOINT / KEY を設定して明示的に実行する")]
    public class AzureOpenAIEmbeddingProviderRealTest
    {
        public const string DefaultDeployment = "text-embedding-3-small";

        public static AzureOpenAIEmbeddingSettings SettingsFromEnvironment(int dimensions = 0) => new()
        {
            EndPoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty,
            Key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? string.Empty,
            Deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") ?? DefaultDeployment,
            Dimensions = dimensions,
        };

        [Test]
        public async Task 埋め込みモデルに到達しベクトルが返る()
        {
            var settings = SettingsFromEnvironment();
            if (string.IsNullOrEmpty(settings.EndPoint) || string.IsNullOrEmpty(settings.Key)) Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY が未設定");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var vectors = await new AzureOpenAIEmbeddingProvider(settings).EmbedAsync(["納期が遅れている注文", "請求書の再発行"], cts.Token);
            Assert.That(vectors.Count, Is.EqualTo(2));
            TestContext.Out.WriteLine($"dimensions = {vectors[0].Length}");
            Assert.That(vectors[0].Length, Is.GreaterThan(0));
            Assert.That(vectors[1].Length, Is.EqualTo(vectors[0].Length));

            //text-embedding-3 系は次元を縮められる
            var small = await new AzureOpenAIEmbeddingProvider(SettingsFromEnvironment(dimensions: 256)).EmbedAsync(["納期"], cts.Token);
            Assert.That(small[0].Length, Is.EqualTo(256));
        }
    }
}
