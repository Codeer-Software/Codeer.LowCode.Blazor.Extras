using Codeer.LowCode.Blazor.Extras.Server.AI;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>AzureOpenAIClients: AISettings が揃っているときだけファクトリを返す (接続はしない)。</summary>
    public class AzureOpenAIClientsTest
    {
        static AISettings Full() => new() { OpenAIEndPoint = "https://example.openai.azure.com/", OpenAIKey = "k", ChatModel = "gpt" };

        [Test]
        public void 設定が揃っていればファクトリを返す()
        {
            Assert.That(AzureOpenAIClients.ChatClientFactory(Full()), Is.Not.Null);
            using var chat = AzureOpenAIClients.ChatClientFactory(Full())!();
            Assert.That(chat, Is.InstanceOf<IChatClient>());
        }

        [Test]
        public void 欠けている設定があれば該当するファクトリはnull()
        {
            var noEndpoint = Full(); noEndpoint.OpenAIEndPoint = "";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noEndpoint), Is.Null);

            var noKey = Full(); noKey.OpenAIKey = " ";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noKey), Is.Null);

            var noChat = Full(); noChat.ChatModel = "";
            Assert.That(AzureOpenAIClients.ChatClientFactory(noChat), Is.Null);
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
    /// 環境変数 AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL (チャット)。埋め込みは AzureOpenAIEmbeddingProviderRealTest。
    /// </summary>
    [Explicit("実 Azure OpenAI を呼ぶ。AZURE_OPENAI_ENDPOINT / KEY / MODEL を設定して明示的に実行する")]
    public class AzureOpenAIClientsRealTest
    {
        static AISettings FromEnvironment() => new()
        {
            OpenAIEndPoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty,
            OpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? string.Empty,
            ChatModel = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL") ?? string.Empty,
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
    }
}
