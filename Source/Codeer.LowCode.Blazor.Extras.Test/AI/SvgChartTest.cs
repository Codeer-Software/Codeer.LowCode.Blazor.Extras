using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using System.Xml.Linq;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    public class SvgChartTest
    {
        static XElement Parse(string svg) => XElement.Parse(svg);

        [Test]
        public void 棒グラフは系列数と項目数ぶんの矩形と凡例を持ち整形式のSVGになる()
        {
            var svg = SvgChart.Render(SvgChart.ChartType.Bar, "売上 <2026>", new[] { "1月", "2月", "3月" },
                new[] { new SvgChart.Series("A", new double?[] { 10, 20, 30 }), new SvgChart.Series("B", new double?[] { 5, null, 15 }) });
            var root = Parse(svg);
            var ns = root.Name.Namespace;
            var rects = root.Descendants(ns + "rect").Where(r => r.Element(ns + "title") != null).ToList();
            Assert.That(rects.Count, Is.EqualTo(5), "null は描かない");
            Assert.That(svg, Does.Contain("売上 &lt;2026&gt;"), "タイトルはエスケープ");
            Assert.That(root.Descendants(ns + "text").Any(t => t.Value == "A"), Is.True, "凡例");
            Assert.That(root.Attribute("style")!.Value, Does.Contain("max-width:100%"));
        }

        [Test]
        public void 折れ線は系列ごとにpolylineを持つ()
        {
            var svg = SvgChart.Render(SvgChart.ChartType.Line, "", new[] { "a", "b", "c", "d" },
                new[] { new SvgChart.Series("s1", new double?[] { 1, 2, 3, 4 }), new SvgChart.Series("s2", new double?[] { 4, 3, 2, 1 }) });
            var root = Parse(svg);
            Assert.That(root.Descendants(root.Name.Namespace + "polyline").Count(), Is.EqualTo(2));
            Assert.That(root.Descendants(root.Name.Namespace + "circle").Count(), Is.EqualTo(8));
        }

        [Test]
        public void 円グラフは正の値だけを扇にし割合を表示する()
        {
            var svg = SvgChart.Render(SvgChart.ChartType.Pie, "構成", new[] { "x", "y", "z" },
                new[] { new SvgChart.Series("v", new double?[] { 75, 25, 0 }) });
            var root = Parse(svg);
            Assert.That(root.Descendants(root.Name.Namespace + "path").Count(), Is.EqualTo(2));
            Assert.That(svg, Does.Contain("75%"));
            Assert.That(svg, Does.Contain("25%"));
        }

        //金額のような桁の多い目盛りでも左端が切れない: 目盛り文字の右端 (x, text-anchor=end) がその文字幅ぶん以上 右にある
        [Test]
        public void 縦軸の目盛りは桁数に応じて左マージンが広がり切れない()
        {
            var svg = SvgChart.Render(SvgChart.ChartType.Bar, "", new[] { "a", "b" }, new[] { new SvgChart.Series("s", new double?[] { 3_500_000, 8_000_000 }) });
            var root = Parse(svg);
            var ns = root.Name.Namespace;
            var ticks = root.Descendants(ns + "text").Where(t => t.Attribute("text-anchor")?.Value == "end" && t.Attribute("transform") == null).ToList();
            Assert.That(ticks.Select(t => t.Value), Does.Contain("8,000,000"));
            foreach (var tick in ticks)
            {
                var x = double.Parse(tick.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(x, Is.GreaterThanOrEqualTo(tick.Value.Length * 7.2), $"目盛り {tick.Value} が左に切れる (x={x})");
            }
            //小さい値のときは既定の余白のまま (むやみに広げない)
            var small = Parse(SvgChart.Render(SvgChart.ChartType.Bar, "", new[] { "a" }, new[] { new SvgChart.Series("s", new double?[] { 5 }) }));
            var smallX = small.Descendants(ns + "text").Where(t => t.Attribute("text-anchor")?.Value == "end").Select(t => double.Parse(t.Attribute("x")!.Value, System.Globalization.CultureInfo.InvariantCulture)).First();
            Assert.That(smallX, Is.EqualTo(56));
        }

        [Test]
        public void 負の値があってもゼロ線が引かれ描ける()
        {
            var svg = SvgChart.Render(SvgChart.ChartType.Bar, "", new[] { "a", "b" }, new[] { new SvgChart.Series("s", new double?[] { -5, 10 }) });
            Parse(svg);
            Assert.That(svg, Does.Contain("<rect"));
        }
    }
}
