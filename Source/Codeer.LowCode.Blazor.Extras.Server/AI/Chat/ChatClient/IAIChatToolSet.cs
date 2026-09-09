using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient
{
    /// <summary>
    /// <see cref="ChatClientAgent"/> に組み込むツールの束 (ライブラリ内部の組み立て単位)。Agent は「システムプロンプト + ツールセットの組み合わせ」で作る。
    /// 標準は <see cref="RawDataAccess.RawDataAccessToolSet"/> (スキーマ取得 + SQL 実行) と <see cref="ChartToolSet"/> (SVG グラフ)。
    /// アプリに見せる Agent の入口は <see cref="IAIChatAgent"/> だけ。独自ツールを持つ Agent が必要なら IAIChatAgent を直接実装する。
    /// </summary>
    internal interface IAIChatToolSet
    {
        /// <summary>システムプロンプトに追記する、このツール群の使い方。空でもよい。</summary>
        string Instructions { get; }

        /// <summary>ターンごとにツールを作る (ツールは context を閉じ込めて進捗報告・ログ・成果物の受け渡しをする)。</summary>
        IEnumerable<AITool> CreateTools(AIChatToolContext context);

        /// <summary>返事を HTML にした後の差し込み (グラフのプレースホルダ置換等)。既定は何もしない。</summary>
        string PostProcessHtml(string html, AIChatToolContext context) => html;
    }
}
