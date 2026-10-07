using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>TagField。付いているタグ (チップ)・入力欄・入力中に出る候補。data-role 属性で要素を引く。</summary>
    public class TagFieldDriver : ComponentBase
    {
        /// <summary>入力欄。閲覧専用時は無い。</summary>
        public TextBoxDriver Input => ByCssSelector("[data-role='input']").Wait();

        /// <summary>付いているタグ (左から順)。</summary>
        public IReadOnlyList<string> Tags => Element.FindElements(By.CssSelector("[data-role='tag']")).Select(TagText).ToList();

        /// <summary>入力中に出ている候補 (上から順)。</summary>
        public IReadOnlyList<string> Candidates => Element.FindElements(By.CssSelector("[data-role='candidate']")).Select(e => e.Text).ToList();

        public bool IsViewOnly => Element.FindElements(By.CssSelector("[data-role='view']")).Count > 0;

        /// <summary>タグを打って Enter で確定し、チップになるまで待つ。</summary>
        public void Add(string tag, int timeoutSeconds = 10)
        {
            Input.Element.SendKeys(tag + Keys.Enter);
            WaitFor(() => Tags.Contains(tag.Trim()), timeoutSeconds, "the tag was not added.");
        }

        /// <summary>文字を打って候補を出し、その候補を選ぶ。</summary>
        public void Pick(string text, string candidate, int timeoutSeconds = 10)
        {
            Input.Element.SendKeys(text);
            WaitFor(() => Candidates.Contains(candidate), timeoutSeconds, "the candidate did not appear.");
            Element.FindElements(By.CssSelector("[data-role='candidate']")).First(e => e.Text == candidate).Click();
            WaitFor(() => Tags.Contains(candidate), timeoutSeconds, "the candidate was not added.");
        }

        /// <summary>そのタグの × で外す。</summary>
        public void Remove(string tag, int timeoutSeconds = 10)
        {
            var chip = Element.FindElements(By.CssSelector("[data-role='tag']")).First(e => TagText(e) == tag);
            chip.FindElement(By.CssSelector("[data-role='remove']")).Click();
            WaitFor(() => !Tags.Contains(tag), timeoutSeconds, "the tag was not removed.");
        }

        //編集時のチップには × のボタンも入るので、文字の要素があればそれを読む
        static string TagText(IWebElement chip) => chip.FindElements(By.CssSelector(".tag-chip-text")).FirstOrDefault()?.Text ?? chip.Text;

        static void WaitFor(Func<bool> condition, int timeoutSeconds, string message)
        {
            var end = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < end)
            {
                if (condition()) return;
                Thread.Sleep(200);
            }
            throw new TimeoutException("TagField: " + message);
        }

        public TagFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator TagFieldDriver(ElementFinder finder) => finder.Find<TagFieldDriver>();
    }

    /// <summary>TagField の検索欄。タグの操作は TagFieldDriver と同じで、一致の選択 (すべて含む / いずれかを含む) を持つ。</summary>
    public class TagFieldSearchDriver : TagFieldDriver
    {
        /// <summary>一致の選択。IsSimpleSearchParameter のときは無い。</summary>
        public DropDownListDriver Match => ByCssSelector("[data-role='match']").Wait();

        public TagFieldSearchDriver(IWebElement element) : base(element) { }
        public static implicit operator TagFieldSearchDriver(ElementFinder finder) => finder.Find<TagFieldSearchDriver>();
    }

    /// <summary>TagInputField (保存しない入力欄)。入力部品は TagField と同じなので操作も同じ。</summary>
    public class TagInputFieldDriver : TagFieldDriver
    {
        public TagInputFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator TagInputFieldDriver(ElementFinder finder) => finder.Find<TagInputFieldDriver>();
    }
}
