namespace Codeer.LowCode.Blazor.Extras.AIChat
{
    /// <summary>
    /// AIChatField からサーバーへの送信 (POST {EndPoint})。
    /// サーバーは即時に requestId (<see cref="AIChatSendResponse"/>) を返し、以降はクライアントが
    /// <see cref="AIChatStatusResponse"/> をポーリングする。会話の履歴は conversationId でサーバー (Agent) が保持し、
    /// クライアントは全履歴を送らない。確定した返事は常に HTML (Markdown / テキストはサーバー側で HTML に変換する)。
    /// </summary>
    public class AIChatSendRequest
    {
        /// <summary>会話の識別子。クライアントが生成し「新しい会話」で振り直す。</summary>
        public string ConversationId { get; set; } = string.Empty;
        /// <summary>ユーザーの発言 (プレーンテキスト)。</summary>
        public string Message { get; set; } = string.Empty;
    }
}
