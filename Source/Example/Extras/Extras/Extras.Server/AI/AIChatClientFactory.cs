using Azure;
using Azure.AI.OpenAI;
using Codeer.LowCode.Blazor.Extras.Server.AI;
using Microsoft.Extensions.AI;

namespace Extras.Server.AI
{
    /// <summary>
    /// AIChat の Agent に渡す IChatClient の作り方 (アプリの持ち物)。ライブラリは IChatClient 抽象しか知らないので、
    /// どのプロバイダ (Azure OpenAI / OpenAI / Ollama …) を使うかはここで決める。このサンプルは AISettings の Azure OpenAI。
    /// </summary>
    internal static class AIChatClientFactory
    {
        /// <summary>AISettings の OpenAIEndPoint / OpenAIKey / ChatModel が揃っているときだけファクトリを返す (欠けていれば null = AI Agent を登録しない)。</summary>
        public static Func<IChatClient>? CreateAzureOpenAI(AISettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OpenAIEndPoint) || string.IsNullOrWhiteSpace(settings.OpenAIKey) || string.IsNullOrWhiteSpace(settings.ChatModel))
                return null;
            var client = new AzureOpenAIClient(new Uri(settings.OpenAIEndPoint), new AzureKeyCredential(settings.OpenAIKey));
            return () => client.GetChatClient(settings.ChatModel).AsIChatClient();
        }
    }
}
