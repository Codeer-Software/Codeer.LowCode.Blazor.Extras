using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// Microsoft.Extensions.AI の <see cref="IChatClient"/> で返事を作る標準 Agent。
    /// 「システムプロンプト + ツールセット (<see cref="IAIChatToolSet"/>)」の組み合わせで振る舞いが決まり、
    /// 会話履歴の保持、ツール呼び出し (function calling) の往復、逐次表示、Markdown → HTML と後処理までを担う。
    /// モデルの選択と認証はアプリの責務 (Azure OpenAI / OpenAI / Ollama 等の IChatClient を渡す)。
    /// <code>
    /// var agent = new ChatClientAgent(() => azureClient.GetChatClient("gpt-4o").AsIChatClient(), new ChatClientAgentOptions
    /// {
    ///     SystemPrompt = "...",
    ///     ToolSets = { new RawDataAccessToolSet(rawOptions), new ChartToolSet() },
    /// });
    /// </code>
    /// 名前付きの派生 (<see cref="RawDataAccessAgent"/> 等) はこのクラスの設定済みインスタンス。
    /// </summary>
    public class ChatClientAgent : IAIChatAgent
    {
        readonly Func<IChatClient> _clientFactory;
        readonly ChatClientAgentOptions _options;
        readonly ConversationHistory _history;
        readonly ILogger? _logger;

        public ChatClientAgent(Func<IChatClient> clientFactory, ChatClientAgentOptions? options = null)
        {
            _clientFactory = clientFactory;
            _options = options ?? new ChatClientAgentOptions();
            _history = new ConversationHistory(_options.MaxHistoryTurns, _options.HistoryRetention);
            _logger = _options.LoggerFactory?.CreateLogger(GetType());
        }

        public ChatClientAgentOptions Options => _options;

        /// <summary>保持中の会話数 (テスト・監視用)。</summary>
        public int ConversationCount => _history.Count;

        public virtual async Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
        {
            var context = new AIChatToolContext(request, progress, cancellationToken, _logger);
            var tools = _options.ToolSets.SelectMany(t => t.CreateTools(context)).ToList();

            var messages = new List<ChatMessage> { new(ChatRole.System, BuildSystemPrompt(request)) };
            messages.AddRange(_history.Get(request.ConversationId));
            var userMessage = new ChatMessage(ChatRole.User, request.Message);
            messages.Add(userMessage);

            var chatOptions = new ChatOptions();
            if (tools.Count > 0) chatOptions.Tools = tools;

            var client = CreateClient();
            var text = new StringBuilder();
            var updates = new List<ChatResponseUpdate>();
            var lastProgress = string.Empty;
            await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
            {
                updates.Add(update);
                foreach (var call in update.Contents.OfType<FunctionCallContent>())
                {
                    lastProgress = $"{call.Name} …";
                    progress.Report(lastProgress);
                    _logger?.LogInformation("AIChat tool call {Tool} by {User} (conversation {Conversation})", call.Name, request.UserName, request.ConversationId);
                }
                if (update.Text.Length == 0) continue;
                if (lastProgress.Length > 0) { lastProgress = string.Empty; progress.Report(string.Empty); }
                text.Append(update.Text);
                if (_options.StreamPartialReplies) progress.ReportPartial(AIChatReply.Html(ToHtml(text.ToString(), context)));
            }

            var response = updates.ToChatResponse();
            var final = text.Length > 0 ? text.ToString() : response.Text;
            _history.Append(request.ConversationId, userMessage, response.Messages);
            return AIChatReply.Html(ToHtml(final, context));
        }

        /// <summary>会話履歴を捨てる (通常はクライアントが conversationId を振り直すので不要。明示的に切りたいとき用)。</summary>
        public void ForgetConversation(string conversationId) => _history.Remove(conversationId);

        IChatClient CreateClient()
        {
            var inner = _clientFactory();
            if (_options.ToolSets.Count == 0) return inner;
            return new ChatClientBuilder(inner)
                .UseFunctionInvocation(_options.LoggerFactory, c =>
                {
                    c.MaximumIterationsPerRequest = _options.MaxToolCallRoundsPerReply;
                    c.IncludeDetailedErrors = true;
                })
                .Build();
        }

        string BuildSystemPrompt(AIChatAgentRequest request)
        {
            var sb = new StringBuilder(_options.SystemPrompt);
            foreach (var toolSet in _options.ToolSets)
            {
                if (string.IsNullOrWhiteSpace(toolSet.Instructions)) continue;
                sb.AppendLine().AppendLine().Append(toolSet.Instructions.Trim());
            }
            if (!string.IsNullOrEmpty(request.UserName))
                sb.AppendLine().AppendLine().Append("The current user is \"").Append(request.UserName).Append("\".");
            return sb.ToString();
        }

        string ToHtml(string markdown, AIChatToolContext context)
        {
            var html = ChatReplyHtml.FromMarkdown(markdown);
            foreach (var toolSet in _options.ToolSets) html = toolSet.PostProcessHtml(html, context);
            return html;
        }
    }
}
