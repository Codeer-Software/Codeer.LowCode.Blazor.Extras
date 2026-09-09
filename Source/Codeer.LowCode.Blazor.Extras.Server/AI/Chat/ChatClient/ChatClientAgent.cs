using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient
{
    /// <summary>
    /// Microsoft.Extensions.AI の <see cref="IChatClient"/> で返事を作る標準 Agent。システムプロンプトで振る舞いを決め、
    /// 会話履歴の保持 (会話 ID ごと・保持期限つき)、逐次表示、Markdown → HTML までを担う。
    /// モデルの選択と認証はアプリの責務 (Azure OpenAI / OpenAI / Ollama 等の IChatClient を渡す)。
    /// <code>
    /// var agent = new ChatClientAgent(() => azureClient.GetChatClient("gpt-4o").AsIChatClient(),
    ///     new ChatClientAgentOptions { SystemPrompt = "..." });
    /// </code>
    /// ツール (function calling) を持つ Agent (<see cref="RawDataAccess.RawDataAccessAgent"/>) は、このクラスを中に持ってライブラリ内部のツールセットを Options に足し、委譲する。
    /// アプリ独自のツールを持つ Agent は <see cref="IAIChatAgent"/> を直接実装する。
    /// </summary>
    public sealed class ChatClientAgent : IAIChatAgent
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

        /// <summary>保持中の会話数 (テスト用)。</summary>
        internal int ConversationCount => _history.Count;

        public async Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
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
                    lastProgress = string.Format(Resources.AIChat_ToolRunning, call.Name);
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
                sb.AppendLine().AppendLine().Append("現在のユーザー: ").Append(request.UserName);
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
