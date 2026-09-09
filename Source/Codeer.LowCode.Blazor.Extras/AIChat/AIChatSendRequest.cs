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
        /// <summary>デザインで指定した Agent 名 (空なら既定)。サーバーはこの名前で返事を作る Agent を選ぶ。</summary>
        public string Agent { get; set; } = string.Empty;
        /// <summary>デザインで指定した補足文書のフォルダ (Resources からの相対パス。空なら文書なし)。</summary>
        public string DocumentFolder { get; set; } = string.Empty;
        /// <summary>
        /// クライアントが表示しているこれまでの会話の写し (テキストのみ・直近数往復・文字数上限あり)。
        /// サーバー側の会話履歴が保持期限や再起動で消えていたときに文脈を取り戻すための保険で、履歴が残っていればサーバーは無視する。
        /// </summary>
        public List<AIChatTranscriptMessage> Transcript { get; set; } = new();
    }
}
