using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>カード。中身はカードレイアウトの Module なので、生成した DetailLayout ページオブジェクトで読む。</summary>
    public class TaskBoardCardDriver : ComponentBase
    {
        public TDetailLayout Layout<TDetailLayout>() where TDetailLayout : ComponentBase => new ElementFinder(Element).Find<TDetailLayout>();
        public string Text => Element.TextContent();
        /// <summary>ダブルクリックで編集ダイアログ (PopupLayout) を開く。</summary>
        public void DoubleClick() => Element.DoubleClick();
        public TaskBoardCardDriver(IWebElement element) : base(element) { }
        public static implicit operator TaskBoardCardDriver(ElementFinder finder) => finder.Find<TaskBoardCardDriver>();
    }

    public class TaskBoardColumnDriver : ComponentBase
    {
        public string Title => ByCssSelector(".taskboard-column-title").Wait().Find().TextContent();
        public ButtonDriver Add => ByCssSelector(".taskboard-add-btn").Wait();
        public IReadOnlyList<TaskBoardCardDriver> Cards => Element.FindElements(By.CssSelector(".taskboard-card")).Select(e => new TaskBoardCardDriver(e)).ToList();
        public TaskBoardColumnDriver(IWebElement element) : base(element) { }
        public static implicit operator TaskBoardColumnDriver(ElementFinder finder) => finder.Find<TaskBoardColumnDriver>();
    }

    public class TaskBoardFieldDriver : ComponentBase
    {
        public ItemsControlDriver<TaskBoardColumnDriver> Columns => ByCssSelector(".taskboard-columns").Wait().Find<ItemsControlDriver<TaskBoardColumnDriver>>();
        public TaskBoardColumnDriver FindColumn(string title) => Columns.AsEnumerable().First(c => c.Title == title);
        public TaskBoardFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator TaskBoardFieldDriver(ElementFinder finder) => finder.Find<TaskBoardFieldDriver>();
    }
}
