using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// ChatClientAgent のテスト用 IChatClient。呼ばれるたびに台本 (<see cref="Script"/>) を先頭から 1 つ消費して返す。
    /// 台本の要素はテキスト (数文字ずつストリーム) か FunctionCallContent (ツール呼び出し)。受け取ったメッセージ列は <see cref="Calls"/> に残す。
    /// </summary>
    public class FakeChatClient : IChatClient
    {
        public Queue<IList<AIContent>> Script { get; } = new();
        public List<List<ChatMessage>> Calls { get; } = new();
        public List<ChatOptions?> Options { get; } = new();

        public FakeChatClient Text(string text) { Script.Enqueue(new List<AIContent> { new TextContent(text) }); return this; }

        public FakeChatClient Call(string name, IDictionary<string, object?> args, string callId = "call1")
        {
            Script.Enqueue(new List<AIContent> { new FunctionCallContent(callId, name, args) });
            return this;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            Options.Add(options);
            var contents = Script.Count > 0 ? Script.Dequeue() : new List<AIContent> { new TextContent("(no script)") };
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(messages.ToList());
            Options.Add(options);
            var contents = Script.Count > 0 ? Script.Dequeue() : new List<AIContent> { new TextContent("(no script)") };
            foreach (var content in contents)
            {
                if (content is TextContent text)
                {
                    for (var i = 0; i < text.Text.Length; i += 3)
                    {
                        await Task.Yield();
                        yield return new ChatResponseUpdate(ChatRole.Assistant, text.Text.Substring(i, Math.Min(3, text.Text.Length - i))) { MessageId = "m" + Calls.Count };
                    }
                }
                else
                {
                    yield return new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { content }) { MessageId = "m" + Calls.Count };
                }
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
