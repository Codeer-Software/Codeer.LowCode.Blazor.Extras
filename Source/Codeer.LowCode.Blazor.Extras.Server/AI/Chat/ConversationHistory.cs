using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// 会話 ID ごとのメッセージ履歴 (プロセス内メモリ)。ツール呼び出しと結果も含めてそのまま持つ
    /// (OpenAI 系は呼び出しと結果の対応が崩れると拒否するので、捨てるときはユーザー発言の境目で切る)。
    /// </summary>
    internal sealed class ConversationHistory
    {
        sealed class Entry
        {
            public List<ChatMessage> Messages { get; } = new();
            public DateTime LastAccess { get; set; } = DateTime.UtcNow;
        }

        readonly Dictionary<string, Entry> _entries = new();
        readonly int _maxTurns;
        readonly TimeSpan _retention;

        public ConversationHistory(int maxTurns, TimeSpan retention)
        {
            _maxTurns = Math.Max(1, maxTurns);
            _retention = retention;
        }

        /// <summary>履歴の写し (呼び出し側がメッセージを足して送る)。</summary>
        public List<ChatMessage> Get(string conversationId)
        {
            lock (_entries)
            {
                Cleanup();
                return _entries.TryGetValue(conversationId, out var entry) ? new List<ChatMessage>(entry.Messages) : new List<ChatMessage>();
            }
        }

        /// <summary>1 ターン分 (ユーザー発言 + 応答のメッセージ列) を追記し、古いターンを捨てる。</summary>
        public void Append(string conversationId, ChatMessage userMessage, IEnumerable<ChatMessage> responseMessages)
        {
            lock (_entries)
            {
                if (!_entries.TryGetValue(conversationId, out var entry)) _entries[conversationId] = entry = new Entry();
                entry.LastAccess = DateTime.UtcNow;
                entry.Messages.Add(userMessage);
                entry.Messages.AddRange(responseMessages);
                Trim(entry.Messages);
            }
        }

        public void Remove(string conversationId)
        {
            lock (_entries) _entries.Remove(conversationId);
        }

        public int Count
        {
            get { lock (_entries) return _entries.Count; }
        }

        void Trim(List<ChatMessage> messages)
        {
            var userIndexes = new List<int>();
            for (var i = 0; i < messages.Count; i++)
                if (messages[i].Role == ChatRole.User) userIndexes.Add(i);
            if (userIndexes.Count <= _maxTurns) return;
            var keepFrom = userIndexes[userIndexes.Count - _maxTurns];
            messages.RemoveRange(0, keepFrom);
        }

        void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _entries.Where(e => now - e.Value.LastAccess > _retention).Select(e => e.Key).ToList())
                _entries.Remove(key);
        }
    }
}
