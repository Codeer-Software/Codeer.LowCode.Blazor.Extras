void DetailLayoutDesign_OnAfterInitialization()
{
    Input.Value = "入力した値 123";
    OnCopiedLog.Value = "(OnCopied はまだ呼ばれていません)";
    OnCopiedLog.IsViewOnly = true;
}

void CopyFixed_OnCopied()
{
    OnCopiedLog.Value = "OnCopied が呼ばれました";
}

void ScriptCopyText_OnClick()
{
    CopyMarkup.CopyText("スクリプトから: " + Input.Value);
}

void ScriptCopy_OnClick()
{
    CopyInput.Copy();
}
