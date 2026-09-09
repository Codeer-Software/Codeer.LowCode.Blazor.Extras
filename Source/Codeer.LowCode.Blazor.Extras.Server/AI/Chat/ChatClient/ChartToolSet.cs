using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient
{
    /// <summary>
    /// グラフを描くツール <c>render_chart</c>。数値を渡すとサーバー側で SVG を作り、返事の中に埋め込むためのプレースホルダ
    /// (<c>[[chart:1]]</c>) を返す。モデルに SVG を書かせないので、数字の写し間違いが起きない。
    /// プレースホルダは <see cref="PostProcessHtml"/> で SVG に置き換える (Markdown の段落に 1 つだけ置かれる想定)。
    /// </summary>
    internal sealed class ChartToolSet : IAIChatToolSet
    {
        const string ItemsKey = "Codeer.AIChat.Charts";
        static readonly Regex _placeholder = new(@"(?:<p>\s*)?\[\[chart:(\d+)\]\](?:\s*</p>)?", RegexOptions.Compiled);

        public string Instructions =>
            "render_chart でグラフ (bar / line / pie) を描けます。ユーザーがグラフを求めたとき、または推移や比較が図のほうが分かりやすいときに使ってください。" +
            "渡す数値は取得したもの (推測ではない) だけにしてください。ツールは [[chart:1]] のようなプレースホルダを返すので、返事の中でグラフを置きたい位置に、その行だけで置いてください。自分でグラフを描いてはいけません。";

        /// <summary>1 系列のデータ。</summary>
        internal class ChartSeries
        {
            [Description("系列名 (凡例に表示)。")]
            public string Name { get; set; } = string.Empty;
            [Description("labels と同じ順で 1 ラベルに 1 値。欠損は null。")]
            public double?[] Values { get; set; } = Array.Empty<double?>();
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            yield return AIFunctionFactory.Create(
                ([Description("bar / line / pie のいずれか")] string chartType,
                 [Description("グラフのタイトル (空でもよい)。")] string title,
                 [Description("X 軸の項目ラベル (円グラフでは各扇)。")] string[] labels,
                 [Description("1 つ以上の系列。円グラフは最初の系列だけを使う。")] ChartSeries[] series)
                    => Render(chartType, title, labels, series, context),
                "render_chart",
                "数値からグラフを描き、返事に置くためのプレースホルダを返す。");
        }

        static string Render(string chartType, string title, string[] labels, ChartSeries[] series, AIChatToolContext context)
        {
            if (labels == null || labels.Length == 0) return "error: labels が空です";
            if (series == null || series.Length == 0) return "error: series が空です";
            var type = (chartType ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "line" => SvgChart.ChartType.Line,
                "pie" => SvgChart.ChartType.Pie,
                _ => SvgChart.ChartType.Bar,
            };
            var svg = SvgChart.Render(type, title ?? string.Empty, labels,
                series.Select(s => new SvgChart.Series(s.Name ?? string.Empty, s.Values ?? Array.Empty<double?>())).ToList());

            var charts = Charts(context);
            var index = charts.Count + 1;
            charts[index] = svg;
            context.Progress.Report(Resources.AIChat_DrawingChart);
            return $"[[chart:{index}]]";
        }

        public string PostProcessHtml(string html, AIChatToolContext context)
        {
            if (!context.Items.ContainsKey(ItemsKey) || string.IsNullOrEmpty(html)) return html;
            var charts = Charts(context);
            return _placeholder.Replace(html, m =>
                charts.TryGetValue(int.Parse(m.Groups[1].Value), out var svg) ? $"<div class=\"aichat-chart\">{svg}</div>" : m.Value);
        }

        static Dictionary<int, string> Charts(AIChatToolContext context)
        {
            if (context.Items.TryGetValue(ItemsKey, out var value) && value is Dictionary<int, string> charts) return charts;
            charts = new Dictionary<int, string>();
            context.Items[ItemsKey] = charts;
            return charts;
        }
    }
}
