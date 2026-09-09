using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// 1 回の返事 (ターン) の間、ツールが共有する文脈。<see cref="ChatClientAgent"/> がターンごとに作り、
    /// <see cref="IAIChatToolSet.CreateTools"/> と <see cref="IAIChatToolSet.PostProcessHtml"/> に渡す。
    /// </summary>
    public class AIChatToolContext
    {
        public AIChatToolContext(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken, ILogger? logger)
        {
            Request = request;
            Progress = progress;
            CancellationToken = cancellationToken;
            Logger = logger;
        }

        /// <summary>依頼 (会話 ID・発言・ユーザー名・Agent 名)。</summary>
        public AIChatAgentRequest Request { get; }

        /// <summary>途中経過の報告先。ツールは「SQL を実行しています…」等をここへ出す。</summary>
        public IAIChatProgress Progress { get; }

        public CancellationToken CancellationToken { get; }

        /// <summary>監査ログ (ツール呼び出しの内容を残す)。未設定なら null。</summary>
        public ILogger? Logger { get; }

        /// <summary>このターンの間だけ生きる置き場。ツールが作った成果物 (グラフ等) を返事の後処理へ渡すのに使う。</summary>
        public Dictionary<string, object> Items { get; } = new();
    }
}
