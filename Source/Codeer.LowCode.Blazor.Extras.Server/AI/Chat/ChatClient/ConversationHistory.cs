using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient
{
    /// <summary>
    /// 会話ごとのメッセージ履歴 (プロセス内メモリ)。鍵は「所有者 + 会話 ID」で、別のユーザーが同じ会話 ID を送っても別の会話になる
    /// (所有者が空 = 認証の無いデモ等では会話 ID だけで引く)。
    /// <para>
    /// トークンの膨張を抑えるため 3 段で切り詰める。いずれもユーザー発言の境目 (ターン) を単位にする
    /// (OpenAI 系はツール呼び出しと結果の対応が崩れると拒否するので、ターンの途中では切らない)。
    /// ① 直近 N ターンより古いターンからはツール呼び出しと結果 (SQL の結果 JSON 等、大きい) を落とし、モデルの文章だけ残す
    /// ② ターン数の上限を超えたら古いターンを捨てる
    /// ③ 文字数の上限を超えたら、上限に収まるまで古いターンを捨てる (直近 1 ターンは残す)
    /// </para>
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
        readonly int _keepToolResultsForTurns;
        readonly int _maxCharacters;
        readonly TimeSpan _retention;

        public ConversationHistory(int maxTurns, int keepToolResultsForTurns, int maxCharacters, TimeSpan retention)
        {
            _maxTurns = Math.Max(1, maxTurns);
            _keepToolResultsForTurns = Math.Max(0, keepToolResultsForTurns);
            _maxCharacters = Math.Max(0, maxCharacters);
            _retention = retention;
        }

        public static string Key(string owner, string conversationId)
            => string.IsNullOrEmpty(owner) ? conversationId : owner + "\n" + conversationId;

        /// <summary>履歴が空の会話に、クライアントの写し (文章だけ) を最初の履歴として入れる。既に履歴があれば何もしない。</summary>
        public void Seed(string key, IEnumerable<ChatMessage> messages)
        {
            lock (_entries)
            {
                if (_entries.TryGetValue(key, out var existing) && existing.Messages.Count > 0) return;
                var entry = new Entry();
                entry.Messages.AddRange(messages);
                _entries[key] = entry;
            }
        }

        /// <summary>履歴の写し (呼び出し側がメッセージを足して送る)。</summary>
        public List<ChatMessage> Get(string key)
        {
            lock (_entries)
            {
                Cleanup();
                return _entries.TryGetValue(key, out var entry) ? new List<ChatMessage>(entry.Messages) : new List<ChatMessage>();
            }
        }

        /// <summary>1 ターン分 (ユーザー発言 + 応答のメッセージ列) を追記し、切り詰める。</summary>
        public void Append(string key, ChatMessage userMessage, IEnumerable<ChatMessage> responseMessages)
        {
            lock (_entries)
            {
                if (!_entries.TryGetValue(key, out var entry)) _entries[key] = entry = new Entry();
                entry.LastAccess = DateTime.UtcNow;
                entry.Messages.Add(userMessage);
                entry.Messages.AddRange(responseMessages);
                Trim(entry.Messages);
            }
        }

        public int Count
        {
            get { lock (_entries) return _entries.Count; }
        }

        void Trim(List<ChatMessage> messages)
        {
            var turns = SplitTurns(messages);

            //① 古いターンのツール呼び出し・結果を落とす (文章だけ残す)
            for (var i = 0; i < turns.Count - _keepToolResultsForTurns; i++) turns[i] = StripToolMessages(turns[i]);

            //② ターン数
            if (turns.Count > _maxTurns) turns.RemoveRange(0, turns.Count - _maxTurns);

            //③ 文字数 (直近 1 ターンは残す)
            if (_maxCharacters > 0)
            {
                while (turns.Count > 1 && turns.Sum(Characters) > _maxCharacters) turns.RemoveAt(0);
            }

            messages.Clear();
            foreach (var turn in turns) messages.AddRange(turn);
        }

        //ユーザー発言で始まる塊に分ける (先頭にユーザー発言以外があれば最初の塊に含める)
        static List<List<ChatMessage>> SplitTurns(List<ChatMessage> messages)
        {
            var turns = new List<List<ChatMessage>>();
            foreach (var message in messages)
            {
                if (message.Role == ChatRole.User || turns.Count == 0) turns.Add(new List<ChatMessage>());
                turns[^1].Add(message);
            }
            return turns;
        }

        static List<ChatMessage> StripToolMessages(List<ChatMessage> turn)
        {
            var result = new List<ChatMessage>(turn.Count);
            foreach (var message in turn)
            {
                if (message.Role == ChatRole.Tool) continue;
                if (!message.Contents.Any(c => c is FunctionCallContent || c is FunctionResultContent))
                {
                    result.Add(message);
                    continue;
                }
                var kept = message.Contents.Where(c => c is not FunctionCallContent && c is not FunctionResultContent).ToList();
                if (kept.Count == 0 || kept.All(c => c is TextContent t && string.IsNullOrWhiteSpace(t.Text))) continue;
                result.Add(new ChatMessage(message.Role, kept) { AuthorName = message.AuthorName, MessageId = message.MessageId });
            }
            return result;
        }

        /// <summary>トークン量の近似 (文字数)。ツール呼び出しの引数と結果も数える。</summary>
        static int Characters(List<ChatMessage> turn)
        {
            var total = 0;
            foreach (var message in turn)
            {
                foreach (var content in message.Contents)
                {
                    total += content switch
                    {
                        TextContent t => t.Text?.Length ?? 0,
                        FunctionCallContent f => f.Name.Length + (f.Arguments?.Sum(a => a.Key.Length + (a.Value?.ToString()?.Length ?? 0)) ?? 0),
                        FunctionResultContent r => r.Result?.ToString()?.Length ?? 0,
                        _ => 0,
                    };
                }
            }
            return total;
        }

        void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var key in _entries.Where(e => now - e.Value.LastAccess > _retention).Select(e => e.Key).ToList())
                _entries.Remove(key);
        }
    }
}
