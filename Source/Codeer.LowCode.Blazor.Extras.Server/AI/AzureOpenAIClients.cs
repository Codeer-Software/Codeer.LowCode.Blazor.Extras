using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using System.ClientModel;

namespace Codeer.LowCode.Blazor.Extras.Server.AI
{
    /// <summary>
    /// <see cref="AISettings"/> (Azure OpenAI) から Microsoft.Extensions.AI の抽象 (<see cref="IChatClient"/> / <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>) を作る。
    /// ライブラリ本体 (AI チャットの Agent・AITextAnalyzeService・SemanticSearchIndexer) は抽象しか受け取らないので、
    /// Azure OpenAI を使うホストはここで作ったものを渡すだけでよい。別のプロバイダ (OpenAI / Ollama …) を使うときはホストが同じ型を自分で作る。
    /// 設定が揃っていなければ null を返す (= その機能は無効。AI チャットなら Agent 無し、意味検索なら文章だけ保存)。
    /// AzureOpenAIClient は 1 つ作って使い回す (接続の再利用)。
    /// </summary>
    public static class AzureOpenAIClients
    {
        /// <summary>
        /// チャットモデル (<see cref="AISettings.OpenAIEndPoint"/> / <see cref="AISettings.OpenAIKey"/> / <see cref="AISettings.ChatModel"/>) の <see cref="IChatClient"/> の取り方。
        /// 3 つのどれかが空なら null。
        /// </summary>
        public static Func<IChatClient>? ChatClientFactory(AISettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ChatModel)) return null;
            var client = CreateClient(settings);
            if (client == null) return null;
            return () => client.GetChatClient(settings.ChatModel).AsIChatClient();
        }

        /// <summary>
        /// 埋め込みモデル (<see cref="AISettings.OpenAIEndPoint"/> / <see cref="AISettings.OpenAIKey"/> / <see cref="AISettings.EmbeddingModel"/>) の
        /// <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> の取り方 (SemanticSearchField の索引と意味検索に使う)。3 つのどれかが空なら null。
        /// </summary>
        public static Func<IEmbeddingGenerator<string, Embedding<float>>>? EmbeddingGeneratorFactory(AISettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.EmbeddingModel)) return null;
            var client = CreateClient(settings);
            if (client == null) return null;
            var generator = client.GetEmbeddingClient(settings.EmbeddingModel).AsIEmbeddingGenerator();
            return () => generator;
        }

        static AzureOpenAIClient? CreateClient(AISettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OpenAIEndPoint) || string.IsNullOrWhiteSpace(settings.OpenAIKey)) return null;
            return new AzureOpenAIClient(new Uri(settings.OpenAIEndPoint), new ApiKeyCredential(settings.OpenAIKey));
        }
    }
}
