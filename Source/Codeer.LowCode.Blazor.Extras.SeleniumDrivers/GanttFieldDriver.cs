using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using OpenQA.Selenium.Interactions;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>左側ラベル列の1行。</summary>
    public class GanttLabelRowDriver : ComponentBase
    {
        public string Text => ByCssSelector(".gantt-label-text").Wait().Find().TextContent();
        /// <summary>"12.5%" 形式。ProgressField 未設定なら空文字。</summary>
        public string Progress
        {
            get
            {
                var e = Element.FindElements(By.CssSelector(".gantt-label-progress"));
                return e.Count == 0 ? string.Empty : e[0].TextContent();
            }
        }
        /// <summary>行クリックで編集ダイアログ (DetailLayoutName) を開く。</summary>
        public void Click() => Element.Click();
        public GanttLabelRowDriver(IWebElement element) : base(element) { }
        public static implicit operator GanttLabelRowDriver(ElementFinder finder) => finder.Find<GanttLabelRowDriver>();
    }

    /// <summary>タイムライン上のタスクバー (svg rect)。ラベル行と同じ順。</summary>
    public class GanttTaskBarDriver
    {
        public IWebElement Element { get; }
        public GanttTaskBarDriver(IWebElement element) => Element = element;
        public double X => double.Parse(Element.GetAttribute("x")!, System.Globalization.CultureInfo.InvariantCulture);
        public double Width => double.Parse(Element.GetAttribute("width")!, System.Globalization.CultureInfo.InvariantCulture);
        public void DoubleClick() => Element.DoubleClick();
        public void ContextClick() => new Actions(Element.GetDriver()).ContextClick(Element).Perform();
    }

    public class GanttFieldDriver : ComponentBase
    {
        public ButtonDriver Today => ByCssSelector(".gantt-toolbar-btn-today").Wait();
        public ButtonDriver Prev => ByCssSelector(".gantt-toolbar-btn-prev").Wait();
        public ButtonDriver Next => ByCssSelector(".gantt-toolbar-btn-next").Wait();
        public ButtonDriver Add => ByCssSelector(".gantt-toolbar-btn-add").Wait();
        public string Title => ByCssSelector(".gantt-toolbar-title").Wait().Find().TextContent();
        public ItemsControlDriver<ButtonDriver> ViewButtons => ByCssSelector(".gantt-view-switcher").Wait().Find<ItemsControlDriver<ButtonDriver>>();
        public string ActiveView => Element.FindElements(By.CssSelector(".gantt-view-btn.active")).FirstOrDefault()?.TextContent() ?? string.Empty;

        /// <summary>期間指定モードのときの期間ボタン。</summary>
        public ButtonDriver Range => ByCssSelector(".gantt-range-btn").Wait();
        public DateDriver RangeStart => ByCssSelector(".gantt-range-popup-row:nth-child(1) input[type='date']").Wait();
        public DateDriver RangeEnd => ByCssSelector(".gantt-range-popup-row:nth-child(2) input[type='date']").Wait();
        public ButtonDriver RangeOk => ByCssSelector(".gantt-range-popup-ok").Wait();
        public ButtonDriver RangeCancel => ByCssSelector(".gantt-range-popup-cancel").Wait();

        public ItemsControlDriver<GanttLabelRowDriver> Rows => ByCssSelector(".gantt-label-body").Wait().Find<ItemsControlDriver<GanttLabelRowDriver>>();
        public GanttLabelRowDriver FindRow(string text) => Rows.AsEnumerable().First(r => r.Text == text);
        public IReadOnlyList<GanttTaskBarDriver> TaskBars => Element.FindElements(By.CssSelector("rect.gantt-task-bar")).Select(e => new GanttTaskBarDriver(e)).ToList();
        public int DependencyCount => Element.FindElements(By.CssSelector(".gantt-dep-line")).Count;

        /// <summary>バー右クリックで出るコンテキストメニュー。</summary>
        public ItemsControlDriver<ButtonDriver> ContextMenuItems => ByCssSelector(".gantt-context-menu").Wait().Find<ItemsControlDriver<ButtonDriver>>();

        public GanttFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator GanttFieldDriver(ElementFinder finder) => finder.Find<GanttFieldDriver>();
    }
}
