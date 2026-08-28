using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal
{
    internal static class ItemsControlExtensions
    {
        internal static IEnumerable<T> AsEnumerable<T>(this ItemsControlDriver<T> items) where T : ComponentBase
        {
            for (var i = 0; i < items.Count; i++) yield return items.GetItem(i);
        }
    }
}
