using System.Globalization;
using System.Net;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools
{
    /// <summary>
    /// 依存なしの小さな SVG グラフ描画 (棒・折れ線・円)。チャットの返事に埋め込む用途に絞った最小限のもので、
    /// 幅はコンテナに合わせて伸縮する (viewBox + max-width:100%)。
    /// </summary>
    public static class SvgChart
    {
        public enum ChartType { Bar, Line, Pie }

        public sealed record Series(string Name, IReadOnlyList<double?> Values);

        static readonly string[] _palette = { "#4e79a7", "#f28e2b", "#59a14f", "#e15759", "#76b7b2", "#edc948", "#b07aa1", "#ff9da7", "#9c755f", "#bab0ac" };

        const double Width = 640, Height = 360;
        const double MarginLeft = 64, MarginRight = 24, MarginTop = 40, MarginBottom = 64;

        public static string Render(ChartType type, string title, IReadOnlyList<string> labels, IReadOnlyList<Series> series)
        {
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(Width)} {F(Height)}\" width=\"{F(Width)}\" height=\"{F(Height)}\" " +
                      "style=\"max-width:100%;height:auto;font-family:sans-serif;font-size:12px\" role=\"img\">");
            if (!string.IsNullOrEmpty(title))
                sb.Append($"<text x=\"{F(Width / 2)}\" y=\"22\" text-anchor=\"middle\" font-size=\"15\" font-weight=\"600\">{E(title)}</text>");

            switch (type)
            {
                case ChartType.Pie: RenderPie(sb, labels, series[0]); break;
                case ChartType.Line: RenderXY(sb, labels, series, line: true); break;
                default: RenderXY(sb, labels, series, line: false); break;
            }
            sb.Append("</svg>");
            return sb.ToString();
        }

        static void RenderXY(StringBuilder sb, IReadOnlyList<string> labels, IReadOnlyList<Series> series, bool line)
        {
            var showLegend = series.Count > 1;
            var plotBottom = Height - MarginBottom - (showLegend ? 20 : 0);
            var plotTop = MarginTop;
            var plotLeft = MarginLeft;
            var plotRight = Width - MarginRight;
            var plotWidth = plotRight - plotLeft;
            var plotHeight = plotBottom - plotTop;

            var values = series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            var max = values.Count > 0 ? values.Max() : 0;
            var min = values.Count > 0 ? values.Min() : 0;
            if (min > 0) min = 0;
            if (max < 0) max = 0;
            if (max == min) max = min + 1;
            var (niceMin, niceMax, step) = NiceScale(min, max, 5);

            double Y(double v) => plotBottom - (v - niceMin) / (niceMax - niceMin) * plotHeight;

            //目盛りと横線
            for (var v = niceMin; v <= niceMax + step / 2; v += step)
            {
                var y = Y(v);
                sb.Append($"<line x1=\"{F(plotLeft)}\" y1=\"{F(y)}\" x2=\"{F(plotRight)}\" y2=\"{F(y)}\" stroke=\"#e0e0e0\" stroke-width=\"1\"/>");
                sb.Append($"<text x=\"{F(plotLeft - 8)}\" y=\"{F(y + 4)}\" text-anchor=\"end\" fill=\"#555\">{E(FormatTick(v, step))}</text>");
            }
            var zeroY = Y(0);
            sb.Append($"<line x1=\"{F(plotLeft)}\" y1=\"{F(zeroY)}\" x2=\"{F(plotRight)}\" y2=\"{F(zeroY)}\" stroke=\"#888\" stroke-width=\"1\"/>");

            var n = labels.Count;
            var slot = plotWidth / n;

            //X ラベル (多いときは間引き・回転)
            var every = Math.Max(1, (int)Math.Ceiling(n / 12.0));
            var rotate = n > 8;
            for (var i = 0; i < n; i += every)
            {
                var x = plotLeft + slot * (i + 0.5);
                var label = Truncate(labels[i], 14);
                sb.Append(rotate
                    ? $"<text x=\"{F(x)}\" y=\"{F(plotBottom + 14)}\" text-anchor=\"end\" fill=\"#333\" transform=\"rotate(-35 {F(x)} {F(plotBottom + 14)})\">{E(label)}</text>"
                    : $"<text x=\"{F(x)}\" y=\"{F(plotBottom + 18)}\" text-anchor=\"middle\" fill=\"#333\">{E(label)}</text>");
            }

            if (line)
            {
                for (var s = 0; s < series.Count; s++)
                {
                    var color = _palette[s % _palette.Length];
                    var points = new StringBuilder();
                    for (var i = 0; i < n; i++)
                    {
                        var v = i < series[s].Values.Count ? series[s].Values[i] : null;
                        if (!v.HasValue) continue;
                        var x = plotLeft + slot * (i + 0.5);
                        var y = Y(v.Value);
                        points.Append(F(x)).Append(',').Append(F(y)).Append(' ');
                        sb.Append($"<circle cx=\"{F(x)}\" cy=\"{F(y)}\" r=\"3\" fill=\"{color}\"><title>{E(labels[i])}: {E(FormatValue(v.Value))}</title></circle>");
                    }
                    if (points.Length > 0)
                        sb.Insert(sb.Length, $"<polyline points=\"{points.ToString().TrimEnd()}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
                }
            }
            else
            {
                var groupWidth = slot * 0.7;
                var barWidth = groupWidth / series.Count;
                for (var i = 0; i < n; i++)
                {
                    var groupLeft = plotLeft + slot * i + (slot - groupWidth) / 2;
                    for (var s = 0; s < series.Count; s++)
                    {
                        var v = i < series[s].Values.Count ? series[s].Values[i] : null;
                        if (!v.HasValue) continue;
                        var y = Y(v.Value);
                        var top = Math.Min(y, zeroY);
                        var h = Math.Abs(zeroY - y);
                        var x = groupLeft + barWidth * s;
                        sb.Append($"<rect x=\"{F(x)}\" y=\"{F(top)}\" width=\"{F(Math.Max(1, barWidth - 2))}\" height=\"{F(h)}\" fill=\"{_palette[s % _palette.Length]}\">" +
                                  $"<title>{E(labels[i])}{(series.Count > 1 ? " / " + E(series[s].Name) : "")}: {E(FormatValue(v.Value))}</title></rect>");
                    }
                }
            }

            if (showLegend) RenderLegend(sb, series.Select(s => s.Name).ToList(), Height - 14);
        }

        static void RenderPie(StringBuilder sb, IReadOnlyList<string> labels, Series series)
        {
            var values = new List<(string Label, double Value)>();
            for (var i = 0; i < labels.Count; i++)
            {
                var v = i < series.Values.Count ? series.Values[i] : null;
                if (v.HasValue && v.Value > 0) values.Add((labels[i], v.Value));
            }
            var total = values.Sum(v => v.Value);
            var cx = Width * 0.38; var cy = (Height + MarginTop) / 2 - 10; var r = Math.Min(Width * 0.3, (Height - MarginTop - 40) / 2);
            if (total <= 0)
            {
                sb.Append($"<text x=\"{F(cx)}\" y=\"{F(cy)}\" text-anchor=\"middle\" fill=\"#777\">no data</text>");
                return;
            }
            var angle = -Math.PI / 2;
            for (var i = 0; i < values.Count; i++)
            {
                var sweep = values[i].Value / total * Math.PI * 2;
                var color = _palette[i % _palette.Length];
                if (values.Count == 1)
                {
                    sb.Append($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"{F(r)}\" fill=\"{color}\"/>");
                }
                else
                {
                    var x1 = cx + r * Math.Cos(angle); var y1 = cy + r * Math.Sin(angle);
                    var x2 = cx + r * Math.Cos(angle + sweep); var y2 = cy + r * Math.Sin(angle + sweep);
                    var large = sweep > Math.PI ? 1 : 0;
                    sb.Append($"<path d=\"M {F(cx)} {F(cy)} L {F(x1)} {F(y1)} A {F(r)} {F(r)} 0 {large} 1 {F(x2)} {F(y2)} Z\" fill=\"{color}\" stroke=\"#fff\" stroke-width=\"1\">" +
                              $"<title>{E(values[i].Label)}: {E(FormatValue(values[i].Value))} ({F(values[i].Value / total * 100)}%)</title></path>");
                }
                var mid = angle + sweep / 2;
                if (sweep > 0.25)
                {
                    var lx = cx + r * 0.65 * Math.Cos(mid); var ly = cy + r * 0.65 * Math.Sin(mid);
                    sb.Append($"<text x=\"{F(lx)}\" y=\"{F(ly + 4)}\" text-anchor=\"middle\" fill=\"#fff\" font-size=\"11\">{F(values[i].Value / total * 100)}%</text>");
                }
                angle += sweep;
            }
            //凡例 (右側に縦並び)
            var legendX = Width * 0.72; var legendY = MarginTop + 10;
            for (var i = 0; i < values.Count && i < 14; i++)
            {
                var y = legendY + i * 20;
                sb.Append($"<rect x=\"{F(legendX)}\" y=\"{F(y - 10)}\" width=\"12\" height=\"12\" fill=\"{_palette[i % _palette.Length]}\"/>");
                sb.Append($"<text x=\"{F(legendX + 18)}\" y=\"{F(y)}\" fill=\"#333\">{E(Truncate(values[i].Label, 18))} ({E(FormatValue(values[i].Value))})</text>");
            }
        }

        static void RenderLegend(StringBuilder sb, IReadOnlyList<string> names, double y)
        {
            var x = MarginLeft;
            for (var i = 0; i < names.Count; i++)
            {
                sb.Append($"<rect x=\"{F(x)}\" y=\"{F(y - 10)}\" width=\"12\" height=\"12\" fill=\"{_palette[i % _palette.Length]}\"/>");
                var name = Truncate(names[i], 20);
                sb.Append($"<text x=\"{F(x + 16)}\" y=\"{F(y)}\" fill=\"#333\">{E(name)}</text>");
                x += 16 + name.Length * 7.5 + 18;
            }
        }

        static (double Min, double Max, double Step) NiceScale(double min, double max, int ticks)
        {
            var range = NiceNumber(max - min, false);
            var step = NiceNumber(range / (ticks - 1), true);
            var niceMin = Math.Floor(min / step) * step;
            var niceMax = Math.Ceiling(max / step) * step;
            return (niceMin, niceMax, step);
        }

        static double NiceNumber(double range, bool round)
        {
            if (range <= 0) return 1;
            var exponent = Math.Floor(Math.Log10(range));
            var fraction = range / Math.Pow(10, exponent);
            double nice;
            if (round) nice = fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10;
            else nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
            return nice * Math.Pow(10, exponent);
        }

        static string FormatTick(double v, double step)
        {
            var decimals = step >= 1 ? 0 : Math.Min(6, (int)Math.Ceiling(-Math.Log10(step)));
            return v.ToString("N" + decimals, CultureInfo.InvariantCulture);
        }

        static string FormatValue(double v)
            => Math.Abs(v - Math.Round(v)) < 1e-9 ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("N2", CultureInfo.InvariantCulture);

        static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

        static string F(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

        static string E(string s) => WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
