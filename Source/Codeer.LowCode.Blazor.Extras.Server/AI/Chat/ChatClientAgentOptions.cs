using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary><see cref="ChatClientAgent"/> の設定。</summary>
    public class ChatClientAgentOptions
    {
        /// <summary>
        /// システムプロンプト。ツールセットの <see cref="IAIChatToolSet.Instructions"/> と、依頼したユーザー名がこの後に続く。
        /// 既定は「ユーザーの言語で、Markdown で簡潔に答える」だけの汎用文。
        /// </summary>
        public string SystemPrompt { get; set; } =
            "You are an assistant embedded in a business web application. " +
            "Answer in the user's language, concisely, in Markdown (headings, lists and tables are rendered). " +
            "Do not invent facts; when you do not know, say so.";

        /// <summary>組み込むツールセット (順に説明がプロンプトへ追記される)。</summary>
        public IList<IAIChatToolSet> ToolSets { get; } = new List<IAIChatToolSet>();

        /// <summary>1 回の返事で許すツール呼び出しの往復回数の上限 (暴走とコストの歯止め)。</summary>
        public int MaxToolCallRoundsPerReply { get; set; } = 10;

        /// <summary>会話履歴として保持するユーザー発言の数 (古いターンから捨てる。ツール呼び出しと結果は同じターンとして一緒に扱う)。</summary>
        public int MaxHistoryTurns { get; set; } = 20;

        /// <summary>最後のアクセスからこの時間を過ぎた会話履歴は捨てる。</summary>
        public TimeSpan HistoryRetention { get; set; } = TimeSpan.FromHours(2);

        /// <summary>途中経過として「ここまでの返事」を流すか (モデルがストリーミングに対応していれば逐次表示になる)。</summary>
        public bool StreamPartialReplies { get; set; } = true;

        /// <summary>ツール呼び出しの監査ログとモデル呼び出しのログの出力先。未設定ならログなし。</summary>
        public ILoggerFactory? LoggerFactory { get; set; }
    }
}
