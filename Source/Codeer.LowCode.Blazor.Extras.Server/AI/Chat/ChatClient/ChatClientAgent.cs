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
    /// ライブラリ内部の会話エンジン。公開 Agent (<see cref="RawDataAccess.RawDataAccessAgent"/>) がこのクラスを中に持ってツールセットを Options に足し、委譲する。
    /// アプリ独自の Agent は <see cref="IAIChatAgent"/> を直接実装する (このクラスは公開しない)。
    /// </summary>
    internal sealed class ChatClientAgent
    {
        readonly Func<IChatClient> _clientFactory;
        readonly ChatClientAgentOptions _options;
        readonly ConversationHistory _history;
        readonly ILogger? _logger;

        public ChatClientAgent(Func<IChatClient> clientFactory, ChatClientAgentOptions? options = null)
        {
            _clientFactory = clientFactory;
            _options = options ?? new ChatClientAgentOptions();
            _history = new ConversationHistory(_options.MaxHistoryTurns, _options.KeepToolResultsForTurns, _options.MaxHistoryCharacters, _options.HistoryRetention);
            _logger = _options.LoggerFactory?.CreateLogger(GetType());
        }

        /// <summary>保持中の会話数 (テスト用)。</summary>
        internal int ConversationCount => _history.Count;

        public async Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
        {
            var context = new AIChatToolContext(request, progress, cancellationToken, _logger);
            var tools = _options.ToolSets.SelectMany(t => t.CreateTools(context)).ToList();

            var messages = new List<ChatMessage> { new(ChatRole.System, BuildSystemPrompt(context)) };
            //履歴の鍵は所有者 (ログイン ID) + 会話 ID。別のユーザーが同じ会話 ID を送っても他人の会話には乗れない (所有者が空のデモ等は会話 ID だけ)
            var historyKey = ConversationHistory.Key(request.UserName, request.ConversationId);
            messages.AddRange(_history.Get(historyKey));
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
            _history.Append(historyKey, userMessage, response.Messages);
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

        string BuildSystemPrompt(AIChatToolContext context)
        {
            var request = context.Request;
            var sb = new StringBuilder(_options.SystemPrompt);
            foreach (var toolSet in _options.ToolSets)
            {
                var instructions = toolSet.GetInstructions(context);
                if (string.IsNullOrWhiteSpace(instructions)) continue;
                sb.AppendLine().AppendLine().Append(instructions.Trim());
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
