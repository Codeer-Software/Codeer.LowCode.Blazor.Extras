using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools
{
    /// <summary>
    /// グラフを描くツール <c>render_chart</c>。数値を渡すとサーバー側で SVG を作り、返事の中に埋め込むためのプレースホルダ
    /// (<c>[[chart:1]]</c>) を返す。モデルに SVG を書かせないので、数字の写し間違いが起きない。
    /// プレースホルダは <see cref="PostProcessHtml"/> で SVG に置き換える (Markdown の段落に 1 つだけ置かれる想定)。
    /// </summary>
    public class ChartToolSet : IAIChatToolSet
    {
        const string ItemsKey = "Codeer.AIChat.Charts";
        static readonly Regex _placeholder = new(@"(?:<p>\s*)?\[\[chart:(\d+)\]\](?:\s*</p>)?", RegexOptions.Compiled);

        public string Instructions =>
            "You can draw charts with render_chart (bar, line or pie). Use it when the user asks for a chart, or when a trend or comparison is clearer as a picture. " +
            "Pass the numbers you obtained (never estimates). The tool returns a placeholder like [[chart:1]]; put that placeholder on its own line in your reply where the chart should appear. Do not draw charts yourself.";

        /// <summary>1 系列のデータ。</summary>
        internal class ChartSeries
        {
            [Description("Series name (shown in the legend).")]
            public string Name { get; set; } = string.Empty;
            [Description("One value per label, in the same order as labels. Use null for missing.")]
            public double?[] Values { get; set; } = Array.Empty<double?>();
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            yield return AIFunctionFactory.Create(
                ([Description("bar, line or pie")] string chartType,
                 [Description("Chart title (may be empty).")] string title,
                 [Description("Category labels along the X axis (or pie slices).")] string[] labels,
                 [Description("One or more series. Pie uses the first series only.")] ChartSeries[] series)
                    => Render(chartType, title, labels, series, context),
                "render_chart",
                "Renders a chart from numbers and returns a placeholder to put in the reply.");
        }

        static string Render(string chartType, string title, string[] labels, ChartSeries[] series, AIChatToolContext context)
        {
            if (labels == null || labels.Length == 0) return "error: labels is empty";
            if (series == null || series.Length == 0) return "error: series is empty";
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
            context.Progress.Report("Drawing a chart…");
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
