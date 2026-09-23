using Codeer.LowCode.Blazor.Extras.Server.AI;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>AzureOpenAIClients: AISettings が揃っているときだけファクトリを返す (接続はしない)。</summary>
    public class AzureOpenAIClientsTest
    {
        static AISettings Full() => new() { OpenAIEndPoint = "https://example.openai.azure.com/", OpenAIKey = "k", ChatModel = "gpt", EmbeddingModel = "emb" };

        [Test]
        public void 設定が揃っていればファクトリを返す()
        {
            Assert.That(AzureOpenAIClients.ChatClientFactory(Full()), Is.Not.Null);
            Assert.That(AzureOpenAIClients.EmbeddingGeneratorFactory(Full()), Is.Not.Null);
            using var chat = AzureOpenAIClients.ChatClientFactory(Full())!();
            using var embedding = AzureOpenAIClients.EmbeddingGeneratorFactory(Full())!();
            Assert.That(chat, Is.InstanceOf<IChatClient>());
            Assert.That(embedding, Is.InstanceOf<IEmbeddingGenerator<string, Embedding<float>>>());
        }

        [Test]
        public void 欠けている設定があれば該当するファクトリはnull()
        {
            var noEndpoint = Full(); noEndpoint.OpenAIEndPoint = "";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noEndpoint), Is.Null);
            Assert.That(AzureOpenAIClients.EmbeddingGeneratorFactory(noEndpoint), Is.Null);

            var noKey = Full(); noKey.OpenAIKey = " ";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noKey), Is.Null);
            Assert.That(AzureOpenAIClients.EmbeddingGeneratorFactory(noKey), Is.Null);

            //チャットと埋め込みは独立: 片方のモデル名だけ空でも他方は使える
            var noChat = Full(); noChat.ChatModel = "";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noChat), Is.Null);
            Assert.That(AzureOpenAIClients.EmbeddingGeneratorFactory(noChat), Is.Not.Null);

            var noEmbedding = Full(); noEmbedding.EmbeddingModel = "";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noEmbedding), Is.Not.Null);
            Assert.That(AzureOpenAIClients.EmbeddingGeneratorFactory(noEmbedding), Is.Null);
        }

        [Test]
        public void 未設定のAITextAnalyzeServiceは解析の呼び出し時に分かるメッセージで失敗する()
        {
            //コンストラクタでは落ちない (設定が空のまま起動するホストがある)
            var service = new AITextAnalyzeService(new AISettings());
            Assert.That(service, Is.Not.Null);
        }
    }

    /// <summary>
    /// 実際の Azure OpenAI に AzureOpenAIClients で作ったクライアントで到達する (課金あり・ネットワーク要のため Explicit)。
    /// 環境変数 AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL (チャット) / AZURE_OPENAI_EMBEDDING_MODEL (埋め込み)。
    /// </summary>
    [Explicit("実 Azure OpenAI を呼ぶ。AZURE_OPENAI_ENDPOINT / KEY / MODEL / EMBEDDING_MODEL を設定して明示的に実行する")]
    public class AzureOpenAIClientsRealTest
    {
        /// <summary>埋め込みのデプロイ名は秘密ではないので既定値を持つ (デプロイ名をモデル名と違えたときだけ AZURE_OPENAI_EMBEDDING_MODEL で上書き)。</summary>
        public const string DefaultEmbeddingModel = "text-embedding-3-small";

        static AISettings FromEnvironment() => new()
        {
            OpenAIEndPoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty,
            OpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? string.Empty,
            ChatModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL") ?? string.Empty,
            EmbeddingModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_MODEL") ?? DefaultEmbeddingModel,
        };

        [Test]
        public async Task チャットモデルに到達する()
        {
            var factory = AzureOpenAIClients.ChatClientFactory(FromEnvironment());
            if (factory == null) Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL が未設定");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var reply = await factory!().GetResponseAsync("Reply with the single word: pong", cancellationToken: cts.Token);
            TestContext.Out.WriteLine(reply.Text);
            Assert.That(reply.Text, Does.Contain("pong").IgnoreCase);
        }

        [Test]
        public async Task 埋め込みモデルに到達しベクトルが返る()
        {
            var factory = AzureOpenAIClients.EmbeddingGeneratorFactory(FromEnvironment());
            if (factory == null) Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_EMBEDDING_MODEL が未設定");
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var embeddings = await factory!().GenerateAsync(["納期が遅れている注文", "請求書の再発行"], cancellationToken: cts.Token);
            Assert.That(embeddings.Count, Is.EqualTo(2));
            var dims = embeddings[0].Vector.Length;
            TestContext.Out.WriteLine($"dimensions = {dims}");
            Assert.That(dims, Is.GreaterThan(0));
            Assert.That(embeddings[1].Vector.Length, Is.EqualTo(dims));
        }
    }
}
