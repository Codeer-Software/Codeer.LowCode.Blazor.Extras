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
        /// <summary>見出しをクリックして開閉する (版の直下の summary)。</summary>
        public void Toggle() => ByCssSelector(":scope > summary").Wait().Find().Click();
        public void Open() { if (!IsOpen) Toggle(); }
        /// <summary>開いた状態の差分行 (「表示名: 旧 → 新」の 1 行ずつ。明細は行の内訳を含む)。</summary>
        public IReadOnlyList<string> Changes => Element.FindElements(By.CssSelector(".edit-history-changes > .edit-history-change")).Select(e => e.TextContent()).ToList();
        /// <summary>開いた状態の差分に出ているフィールドの表示名 (最上位だけ。明細の行の中の項目は含まない)。</summary>
        public IReadOnlyList<string> ChangedFieldNames => Element.FindElements(By.CssSelector(".edit-history-changes > .edit-history-change > .edit-history-field")).Select(e => e.TextContent()).ToList();
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

    /// <summary>
    /// 編集履歴フィールド (EditHistoryField)。画面を開いた直後は件数 (「履歴 n 件」) と「表示」だけで、版の一覧は「表示」を押してから読まれる。
    /// Versions / FindVersion / LoadMore は必要なら自動で「表示」を押す (Open) ので、テストは開く操作を書かなくてよい。
    /// </summary>
    public class EditHistoryFieldDriver : ComponentBase
    {
        /// <summary>版 (新しい順)。展開していなければ「表示」を押してから返す。</summary>
        public ItemsControlDriver<EditHistoryVersionDriver> Versions
        {
            get
            {
                Open();
                return ByCssSelector("[data-system='edit-history-versions']").Wait().Find<ItemsControlDriver<EditHistoryVersionDriver>>();
            }
        }
        /// <summary>読み込み中 / 履歴なし / 未保存 などの注記。件数や版が出ているときは空文字。</summary>
        public string Note
        {
            get
            {
                var e = Element.FindElements(By.CssSelector("[data-system='edit-history'] > .edit-history-note"));
                return e.Count == 0 ? string.Empty : e[0].TextContent();
            }
        }
        /// <summary>履歴の件数 (「履歴 n 件」の n)。件数が出ていない (読み込み前・未保存・0 件) ときは 0。</summary>
        public int Count
        {
            get
            {
                var e = Element.FindElements(By.CssSelector("[data-system='edit-history-count']"));
                return e.Count == 0 ? 0 : int.Parse(e[0].GetAttribute("data-count") ?? "0");
            }
        }
        /// <summary>版の一覧を展開しているか。</summary>
        public bool IsOpen => Element.FindElements(By.CssSelector("[data-system='edit-history-collapse']")).Count > 0;
        /// <summary>「表示」(展開前で履歴が 1 件以上あるときだけ出る)。</summary>
        public ButtonDriver OpenButton => ByCssSelector("[data-system='edit-history-open']").Wait();
        /// <summary>「閉じる」(展開中に出る)。</summary>
        public ButtonDriver CloseButton => ByCssSelector("[data-system='edit-history-collapse']").Wait();
        /// <summary>
        /// 版の一覧を展開する。既に展開していれば何もしない。件数の読み込みを待ち、0 件 (「表示」が無い) なら何もしない。
        /// </summary>
        public void Open()
        {
            var limit = DateTime.Now.AddSeconds(30);
            while (true)
            {
                if (IsOpen) return;
                var open = Element.FindElements(By.CssSelector("[data-system='edit-history-open']"));
                if (open.Count > 0)
                {
                    open[0].Click();
                    ByCssSelector("[data-system='edit-history-collapse']").Wait();
                    return;
                }
                //件数が読めていて 0 件、または未保存・詳細以外の注記 = 開くものが無い
                var note = Note;
                if (note.Length != 0 && Element.FindElements(By.CssSelector("[data-system='edit-history-count']")).Count == 0 && !IsLoadingNote(note)) return;
                if (DateTime.Now > limit) throw new Exception("編集履歴の件数が読み込まれません: " + note);
                Thread.Sleep(200);
            }
        }
        static bool IsLoadingNote(string note) => note.Contains("読み込み中") || note.Contains("Loading");
        /// <summary>版が 1 つ以上読み込まれているか (展開していなければ false)。</summary>
        public bool HasVersions => Element.FindElements(By.CssSelector("[data-system='edit-history-version']")).Count > 0;
        /// <summary>「さらに表示」(次のページがあるときだけ出る)。</summary>
        public ButtonDriver LoadMore { get { Open(); return ByCssSelector("[data-system='edit-history-more']").Wait(); } }
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

    /// <summary>履歴モジュール側の「このレコードを復活」ボタン (EditHistoryUndeleteButtonField)。削除の版でだけ描画される。</summary>
    public class EditHistoryUndeleteButtonFieldDriver : ComponentBase
    {
        public bool IsVisible => Element.FindElements(By.CssSelector("[data-system='edit-history-undelete']")).Count > 0;
        public ButtonDriver Button => ByCssSelector("[data-system='edit-history-undelete']").Wait();
        public EditHistoryUndeleteButtonFieldDriver(IWebElement element) : base(element) { }
        public static implicit operator EditHistoryUndeleteButtonFieldDriver(ElementFinder finder) => finder.Find<EditHistoryUndeleteButtonFieldDriver>();
    }

}
