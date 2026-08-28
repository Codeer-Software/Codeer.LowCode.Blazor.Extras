using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>月表示のイベント / 週・日表示の終日イベント・時間イベント。</summary>
    public class CalendarEventDriver : ComponentBase
    {
        /// <summary>表示テキスト (月: .month-event-text / 時間: .time-event-title / 終日: 要素全体)。</summary>
        public string Text
        {
            get
            {
                foreach (var sel in new[] { ".month-event-text", ".time-event-title" })
                {
                    var e = Element.FindElements(By.CssSelector(sel));
                    if (e.Count > 0) return e[0].TextContent();
                }
                return Element.TextContent();
            }
        }
        public string Time
        {
            get
            {
                var e = Element.FindElements(By.CssSelector(".month-event-time, .time-event-time"));
                return e.Count == 0 ? string.Empty : e[0].TextContent();
            }
        }
        public string Tooltip => Element.GetAttribute("title") ?? string.Empty;
        public void Click() => Element.Click();
        public CalendarEventDriver(IWebElement element) : base(element) { }
        public static implicit operator CalendarEventDriver(ElementFinder finder) => finder.Find<CalendarEventDriver>();
    }

    /// <summary>月表示の1日セル。</summary>
    public class CalendarMonthCellDriver : ComponentBase
    {
        public int Day => int.Parse(ByCssSelector(".month-date").Wait().Find().TextContent());
        public bool IsToday => Element.HasClass("month-cell-today");
        public bool IsOtherMonth => Element.HasClass("month-cell-other-month");
        public IReadOnlyList<CalendarEventDriver> Events => Element.FindElements(By.CssSelector(".month-event")).Select(e => new CalendarEventDriver(e)).ToList();
        /// <summary>セルをクリックして追加ダイアログ / OnClickDate を起こす。</summary>
        public void Click() => Element.Click();
        public CalendarMonthCellDriver(IWebElement element) : base(element) { }
        public static implicit operator CalendarMonthCellDriver(ElementFinder finder) => finder.Find<CalendarMonthCellDriver>();
    }

    /// <summary>週・日表示の1日分の時間列。</summary>
    public class CalendarTimeColumnDriver : ComponentBase
    {
        public bool IsToday => Element.HasClass("time-column-today");
        public IReadOnlyList<CalendarEventDriver> Events => Element.FindElements(By.CssSelector(".time-event")).Select(e => new CalendarEventDriver(e)).ToList();
        public void Click() => Element.Click();
        public CalendarTimeColumnDriver(IWebElement element) : base(element) { }
        public static implicit operator CalendarTimeColumnDriver(ElementFinder finder) => finder.Find<CalendarTimeColumnDriver>();
    }

    public class CalendarFieldDriver : ComponentBase
    {
        public ButtonDriver Today => ByCssSelector(".toolbar-btn-today").Wait();
        public ButtonDriver Prev => ByCssSelector(".toolbar-btn-prev").Wait();
        public ButtonDriver Next => ByCssSelector(".toolbar-btn-next").Wait();
        public string Title => ByCssSelector(".toolbar-title").Wait().Find().TextContent();
        /// <summary>表示切替ボタン (日 / 週 / 月 のうち EnableXxxView で有効なもの)。</summary>
        public ItemsControlDriver<ButtonDriver> ViewButtons => ByCssSelector(".view-switcher").Wait().Find<ItemsControlDriver<ButtonDriver>>();
        public string ActiveView => Element.FindElements(By.CssSelector(".view-btn.active")).FirstOrDefault()?.TextContent() ?? string.Empty;

        public bool IsMonthView => Element.FindElements(By.CssSelector(".month-view")).Count > 0;
        public bool IsWeekView => Element.FindElements(By.CssSelector(".week-view")).Count > 0;
        public bool IsDayView => Element.FindElements(By.CssSelector(".day-view")).Count > 0;

        /// <summary>月表示のセル (前後月の埋めセル込み、行順)。</summary>
        public IReadOnlyList<CalendarMonthCellDriver> MonthCells => Element.FindElements(By.CssSelector(".month-cell")).Select(e => new CalendarMonthCellDriver(e)).ToList();
        public CalendarMonthCellDriver FindMonthCell(int day) => MonthCells.First(c => !c.IsOtherMonth && c.Day == day);
        /// <summary>週・日表示の時間列 (週=7列、日=1列)。</summary>
        public IReadOnlyList<CalendarTimeColumnDriver> TimeColumns => Element.FindElements(By.CssSelector(".time-column")).Select(e => new CalendarTimeColumnDriver(e)).ToList();
        /// <summary>週・日表示の終日イベント。</summary>
        public IReadOnlyList<CalendarEventDriver> AllDayEvents => Element.FindElements(By.CssSelector(".allday-event")).Select(e => new CalendarEventDriver(e)).ToList();
        /// <summary>現在の表示に含まれる全イベント。</summary>
        public IReadOnlyList<CalendarEventDriver> Events => Element.FindElements(By.CssSelector(".month-event, .allday-event, .time-event")).Select(e => new CalendarEventDriver(e)).ToList();
        public CalendarEventDriver FindEvent(string text) => Events.First(e => e.Text == text);

        public CalendarFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator CalendarFieldDriver(ElementFinder finder) => finder.Find<CalendarFieldDriver>();
    }
}
