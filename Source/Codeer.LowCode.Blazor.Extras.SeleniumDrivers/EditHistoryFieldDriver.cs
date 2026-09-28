using OpenQA.Selenium;
using Selenium.StandardControls;
using Selenium.StandardControls.PageObjectUtility;
using Selenium.StandardControls.TestAssistant.GeneratorToolKit;
using Codeer.LowCode.Blazor.Extras.SeleniumDrivers.Internal;

namespace Codeer.LowCode.Blazor.Extras.SeleniumDrivers
{
    /// <summary>編集履歴の 1 版 (details 要素)。閉じた状態は見出し (版番号・種別・変更者・日時・要約) だけ。</summary>
    public class EditHistoryVersionDriver : ComponentBase
    {
        /// <summary>「版 3」形式の見出し。</summary>
        public string Number => ByCssSelector(".edit-history-number").Wait().Find().TextContent();
        /// <summary>変更種別の表示 (作成 / 更新 / 削除 / 復活)。</summary>
        public string ChangeType => ByCssSelector(".edit-history-type").Wait().Find().TextContent();
        /// <summary>変更者。契約に UserId が無ければ空文字。</summary>
        public string User => OptionalText(".edit-history-user");
        /// <summary>変更日時。契約に DateTime が無ければ空文字。</summary>
        public string Date => OptionalText(".edit-history-date");
        /// <summary>要約 (「変更 2 項目: 件名, 金額」「レコードが作成されました」など)。</summary>
        public string Summary => ByCssSelector(".edit-history-summary").Wait().Find().TextContent();
        /// <summary>開いているか (details の open)。</summary>
        public bool IsOpen => Element.GetAttribute("open") != null;
        /// <summary>見出しをクリックして開閉する (版の直下の summary。中の「送信内容」の summary は別)。</summary>
        public void Toggle() => ByCssSelector(":scope > summary").Wait().Find().Click();
        public void Open() { if (!IsOpen) Toggle(); }
        /// <summary>開いた状態の差分行 (「表示名: 旧 → 新」の 1 行ずつ。明細は行の内訳を含む)。</summary>
        public IReadOnlyList<string> Changes => Element.FindElements(By.CssSelector(".edit-history-changes > .edit-history-change")).Select(e => e.TextContent()).ToList();
        /// <summary>開いた状態の差分に出ているフィールドの表示名 (最上位だけ。明細の行の中の項目は含まない)。</summary>
        public IReadOnlyList<string> ChangedFieldNames => Element.FindElements(By.CssSelector(".edit-history-changes > .edit-history-change > .edit-history-field")).Select(e => e.TextContent()).ToList();
        /// <summary>受け取った操作 (送信内容 JSON) の折りたたみがあるか (契約に Command 役割があるとき)。</summary>
        public bool HasCommand => Element.FindElements(By.CssSelector("[data-system='edit-history-command']")).Count > 0;
        /// <summary>受け取った操作 (整形された JSON)。無ければ空文字。</summary>
        public string Command => OptionalText("[data-system='edit-history-command'] pre");
        /// <summary>送信内容の折りたたみを開く。</summary>
        public void OpenCommand() => ByCssSelector("[data-system='edit-history-command'] > summary").Wait().Find().Click();
        /// <summary>「この版を表示」(開いた状態で出る)。</summary>
        public ButtonDriver Show => ByCssSelector("[data-system='edit-history-show']").Wait();
        /// <summary>「この版に戻す」。表示専用・最新版・削除の版では出ない。</summary>
        public ButtonDriver Restore => ByCssSelector("[data-system='edit-history-restore']").Wait();
        public bool HasRestore => Element.FindElements(By.CssSelector("[data-system='edit-history-restore']")).Count > 0;

        string OptionalText(string selector)
        {
            var e = Element.FindElements(By.CssSelector(selector));
            return e.Count == 0 ? string.Empty : e[0].TextContent();
        }

        public EditHistoryVersionDriver(IWebElement element) : base(element) { }
        public static implicit operator EditHistoryVersionDriver(ElementFinder finder) => finder.Find<EditHistoryVersionDriver>();
    }

    /// <summary>編集履歴フィールド (EditHistoryField)。版の一覧と「さらに表示」。</summary>
    public class EditHistoryFieldDriver : ComponentBase
    {
        /// <summary>版 (新しい順)。</summary>
        public ItemsControlDriver<EditHistoryVersionDriver> Versions => ByCssSelector("[data-system='edit-history-versions']").Wait().Find<ItemsControlDriver<EditHistoryVersionDriver>>();
        /// <summary>読み込み中 / 履歴なし / 未保存 などの注記。版があるときは空文字。</summary>
        public string Note
        {
            get
            {
                var e = Element.FindElements(By.CssSelector("[data-system='edit-history'] > .edit-history-note"));
                return e.Count == 0 ? string.Empty : e[0].TextContent();
            }
        }
        /// <summary>版が 1 つ以上読み込まれているか。</summary>
        public bool HasVersions => Element.FindElements(By.CssSelector("[data-system='edit-history-version']")).Count > 0;
        /// <summary>「さらに表示」(次のページがあるときだけ出る)。</summary>
        public ButtonDriver LoadMore => ByCssSelector("[data-system='edit-history-more']").Wait();
        public bool HasMore => Element.FindElements(By.CssSelector("[data-system='edit-history-more']")).Count > 0;
        /// <summary>「版 n」の見出しを持つ版を探す。</summary>
        public EditHistoryVersionDriver FindVersion(int number) => Versions.AsEnumerable().First(v => v.Number.EndsWith(" " + number) || v.Number == number.ToString());

        public EditHistoryFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator EditHistoryFieldDriver(ElementFinder finder) => finder.Find<EditHistoryFieldDriver>();
    }

    /// <summary>
    /// 「この版を表示」のダイアログ (本体のモジュールダイアログ)。ページ全体から <c>[data-system='module-dialog']</c> で探す。
    /// 強調された要素 (変更セル / 追加行 / 削除行 / 変更された従属レコードの項目) の数と「閉じる」。
    /// </summary>
    public class EditHistoryVersionDialogDriver : ComponentBase
    {
        public const string Selector = "[data-system='module-dialog']";
        public int ChangedCount => Element.FindElements(By.CssSelector(".edit-history-changed")).Count;
        public int AddedRowCount => Element.FindElements(By.CssSelector(".edit-history-added-row")).Count;
        public int RemovedRowCount => Element.FindElements(By.CssSelector(".edit-history-removed-row")).Count;
        public int ChangedRowCount => Element.FindElements(By.CssSelector(".edit-history-changed-row")).Count;
        /// <summary>フッターのボタン (「閉じる」)。</summary>
        public ButtonDriver Close => ByCssSelector(".modal-footer button").Wait();
        public EditHistoryVersionDialogDriver(IWebElement element) : base(element) { }
        public static implicit operator EditHistoryVersionDialogDriver(ElementFinder finder) => finder.Find<EditHistoryVersionDialogDriver>();
    }

    public static class EditHistoryVersionDialogExtensions
    {
        /// <summary>表示中の「この版を表示」ダイアログを取る。</summary>
        [ComponentObjectIdentify]
        public static EditHistoryVersionDialogDriver AttachEditHistoryVersionDialog(this IWebDriver driver)
            => new MappingBase(driver).ByCssSelector(EditHistoryVersionDialogDriver.Selector).Wait();
    }

    /// <summary>履歴モジュール側の「このレコードを復活」ボタン (EditHistoryRestoreButtonField)。削除の版でだけ描画される。</summary>
    public class EditHistoryRestoreButtonFieldDriver : ComponentBase
    {
        public bool IsVisible => Element.FindElements(By.CssSelector("[data-system='edit-history-restore-record']")).Count > 0;
        public ButtonDriver Button => ByCssSelector("[data-system='edit-history-restore-record']").Wait();
        public EditHistoryRestoreButtonFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator EditHistoryRestoreButtonFieldDriver(ElementFinder finder) => finder.Find<EditHistoryRestoreButtonFieldDriver>();
    }

    /// <summary>履歴モジュール側の対象レコードリンク (EditHistoryTargetLinkField)。開ける行 (削除の版以外) でだけ描画される。</summary>
    public class EditHistoryTargetLinkFieldDriver : ComponentBase
    {
        public bool IsVisible => Element.FindElements(By.CssSelector("[data-system='edit-history-target-link']")).Count > 0;
        public string Text => ByCssSelector("[data-system='edit-history-target-link']").Wait().Find().TextContent();
        /// <summary>リンク先 (対象レコードの詳細 URL)。</summary>
        public string Href => ByCssSelector("[data-system='edit-history-target-link']").Wait().Find().GetAttribute("href") ?? string.Empty;
        public void Click() => ByCssSelector("[data-system='edit-history-target-link']").Wait().Find().Click();
        public EditHistoryTargetLinkFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator EditHistoryTargetLinkFieldDriver(ElementFinder finder) => finder.Find<EditHistoryTargetLinkFieldDriver>();
    }
}
