namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// AIChatField の返事を作る側 (AI Agent) のインターフェース。実装はアプリの持ち物で、
    /// <see cref="AIChatJobStore"/> がバックグラウンドで呼び出し、結果を HTML に正規化してクライアントへ返す。
    /// 会話の履歴は conversationId を鍵に実装側で保持する (クライアントは全履歴を送らない)。
    /// </summary>
    public interface IAIChatAgent
    {
        Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken);
    }

    /// <summary>Agent への 1 回の依頼。</summary>
    public class AIChatAgentRequest
    {
        public string ConversationId { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        /// <summary>依頼したユーザーの識別 (ログイン名等。匿名なら空)。</summary>
        public string UserName { get; init; } = string.Empty;
        /// <summary>AIChatField のデザインで指定された Agent 名 (空なら既定)。複数の Agent を 1 つの実装で受けるときの分岐に使える。</summary>
        public string AgentName { get; init; } = string.Empty;
        /// <summary>AIChatField のデザインで指定された補足文書のフォルダ (Resources からの相対パス。空なら文書なし)。</summary>
        public string DocumentFolder { get; init; } = string.Empty;
    }

    /// <summary>Agent の返事。形式は Agent が申告する。Auto なら内容から判定する (先頭がタグなら HTML、それ以外は Markdown)。</summary>
    public class AIChatReply
    {
        public string Content { get; init; } = string.Empty;
        public AIChatReplyFormat Format { get; init; } = AIChatReplyFormat.Auto;

        public static AIChatReply Text(string text) => new() { Content = text, Format = AIChatReplyFormat.Text };
        public static AIChatReply Markdown(string markdown) => new() { Content = markdown, Format = AIChatReplyFormat.Markdown };
        public static AIChatReply Html(string html) => new() { Content = html, Format = AIChatReplyFormat.Html };
    }

    public enum AIChatReplyFormat
    {
        Auto,
        Text,
        Markdown,
        Html,
    }

    /// <summary>途中経過の報告先。どちらも任意で、報告しなければクライアントは「考え中」だけを表示する。</summary>
    public interface IAIChatProgress
    {
        /// <summary>一言 (「検索しています…」等)。空なら既定の表示に戻る。</summary>
        void Report(string progressText);

        /// <summary>ここまでの返事 (逐次表示用)。呼ぶたびに全体を渡す (差分ではない)。</summary>
        void ReportPartial(AIChatReply partialReply);
    }
}
