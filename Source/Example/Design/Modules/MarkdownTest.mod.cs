void DetailLayoutDesign_OnAfterInitialization()
{
    MdTab.Value = "# 見出し\n\n**太字** と *斜体*、`code`。\n\n| 項目 | 値 |\n|---|---|\n| A | 1 |\n| B | 2 |\n\n- [x] 済み\n- [ ] 未\n\n<b>生のHTMLは文字として見える</b>";
    MdView.IsViewOnly = true;
}

void Copy_OnClick()
{
    MdView.Value = MdTab.Value;
}
