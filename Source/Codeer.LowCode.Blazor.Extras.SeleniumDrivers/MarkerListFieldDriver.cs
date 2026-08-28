using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    public class MarkerDriver : ComponentBase
    {
        public string Label => ByCssSelector(".label").Wait().Find().TextContent();
        /// <summary>style の left/top (px)。</summary>
        public int X => ParsePx("left");
        public int Y => ParsePx("top");
        /// <summary>.marker 自体は幅0のアンカーなので中の丸をクリックする。</summary>
        public void Click() => ByCssSelector(".circle").Wait().Find().Click();
        int ParsePx(string prop)
        {
            var v = Element.GetCssValue(prop);
            return (int)Math.Round(double.Parse(v.Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture));
        }
        public MarkerDriver(IWebElement element) : base(element) { }
        public static implicit operator MarkerDriver(ElementFinder finder) => finder.Find<MarkerDriver>();
    }

    public class MarkerListFieldDriver : ComponentBase
    {
        public IWebElement Image => ByCssSelector("img.base-img").Wait().Find();
        public bool HasImage => Element.FindElements(By.CssSelector("img.base-img")).Count > 0;
        public IReadOnlyList<MarkerDriver> Markers => Element.FindElements(By.CssSelector(".marker")).Select(e => new MarkerDriver(e)).ToList();
        public MarkerDriver FindMarker(string label) => Markers.First(m => m.Label == label);
        /// <summary>画像上の座標 (px、左上基準) をダブルクリックして追加ダイアログ / OnDoubleClickPoint を起こす。</summary>
        public void DoubleClickAt(int x, int y) => ByCssSelector(".img-overlay").Wait().Find().DoubleClickAt(x, y);

        public MarkerListFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator MarkerListFieldDriver(ElementFinder finder) => finder.Find<MarkerListFieldDriver>();
    }
}
