using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal
{
    internal static class ElementExtensions
    {
        internal static IWebDriver GetDriver(this IWebElement element) => ((IWrapsDriver)element).WrappedDriver;

        internal static IJavaScriptExecutor GetJs(this IWebElement element) => (IJavaScriptExecutor)element.GetDriver();

        internal static string TextContent(this IWebElement element)
            => (element.GetAttribute("textContent") ?? string.Empty).Trim();

        internal static bool HasClass(this IWebElement element, string className)
            => (element.GetAttribute("class") ?? string.Empty).Split(' ').Contains(className);

        internal static void DoubleClick(this IWebElement element)
            => new Actions(element.GetDriver()).DoubleClick(element).Perform();

        /// <summary>要素左上基準の座標で dblclick を起こす。Actions は中心基準の丸めで 1px ずれるため、offsetX/Y が正確に届くよう合成イベントで発火する。</summary>
        internal static void DoubleClickAt(this IWebElement element, int offsetX, int offsetY)
            => element.GetJs().ExecuteScript(
                "const r = arguments[0].getBoundingClientRect();" +
                "arguments[0].dispatchEvent(new MouseEvent('dblclick', {bubbles:true, cancelable:true, clientX: r.left + arguments[1], clientY: r.top + arguments[2]}));",
                element, offsetX, offsetY);

        /// <summary>value を書き換えて Blazor の @bind (change) を発火させる。type=color 等 SendKeys が効かない input 用。</summary>
        internal static void SetValueAndChange(this IWebElement element, string value)
            => element.GetJs().ExecuteScript(
                "arguments[0].value = arguments[1]; arguments[0].dispatchEvent(new Event('input', {bubbles:true})); arguments[0].dispatchEvent(new Event('change', {bubbles:true}));",
                element, value);
    }
}
