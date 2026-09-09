using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// <see cref="ChatClientAgent"/> に組み込むツールの束。Agent は「システムプロンプト + ツールセットの組み合わせ」で作る。
    /// ライブラリ標準は <see cref="Tools.RawDataAccessToolSet"/> (スキーマ取得 + SQL 実行) と <see cref="Tools.ChartToolSet"/> (SVG グラフ)。
    /// アプリ固有のツール (社内 API 呼び出し等) はこれを実装して <see cref="ChatClientAgentOptions.ToolSets"/> に足す。
    /// </summary>
    public interface IAIChatToolSet
    {
        /// <summary>システムプロンプトに追記する、このツール群の使い方。空でもよい。</summary>
        string Instructions { get; }

        /// <summary>ターンごとにツールを作る (ツールは context を閉じ込めて進捗報告・ログ・成果物の受け渡しをする)。</summary>
        IEnumerable<AITool> CreateTools(AIChatToolContext context);

        /// <summary>返事を HTML にした後の差し込み (グラフのプレースホルダ置換等)。既定は何もしない。</summary>
        string PostProcessHtml(string html, AIChatToolContext context) => html;
    }
}
