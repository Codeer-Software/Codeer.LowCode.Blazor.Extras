using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>AIChatField。data-role 属性で要素を引く。</summary>
    public class AIChatFieldDriver : ComponentBase
    {
        public TextAreaDriver Input => ByCssSelector("[data-role='input']").Wait();
        public ButtonDriver Send => ByCssSelector("[data-role='send']").Wait();
        public ButtonDriver Stop => ByCssSelector("[data-role='stop']").Wait();
        public ButtonDriver NewConversation => ByCssSelector(".aichat-new").Wait();

        /// <summary>ユーザー発言の吹き出し (上から順)。</summary>
        public IReadOnlyList<IWebElement> UserMessages => Element.FindElements(By.CssSelector("[data-role='user']"));
        /// <summary>確定した返事 (HTML を描いている要素。上から順)。</summary>
        public IReadOnlyList<IWebElement> Replies => Element.FindElements(By.CssSelector("[data-role='reply']"));
        public IReadOnlyList<IWebElement> Errors => Element.FindElements(By.CssSelector("[data-role='error']"));
        public bool IsThinking => Element.FindElements(By.CssSelector("[data-role='thinking']")).Count > 0;

        /// <summary>入力して送信し、返事 (または失敗) が確定するまで待つ。</summary>
        public void SendAndWait(string text, int timeoutSeconds = 60)
        {
            var before = Replies.Count + Errors.Count;
            Input.Edit(text);
            Send.Click();
            var end = DateTime.Now.AddSeconds(timeoutSeconds);
            while (DateTime.Now < end)
            {
                if (!IsThinking && Replies.Count + Errors.Count > before) return;
                Thread.Sleep(200);
            }
            throw new TimeoutException("AIChatField: reply did not arrive.");
        }

        public AIChatFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator AIChatFieldDriver(ElementFinder finder) => finder.Find<AIChatFieldDriver>();
    }
}
