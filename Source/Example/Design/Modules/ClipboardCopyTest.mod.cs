void DetailLayoutDesign_OnAfterInitialization()
{
    Command.Value = "このURLを見て指示に従って https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Starter";
    Command.IsViewOnly = true;
    Input.Value = "入力した値 123";
    OnCopiedLog.Value = "(OnCopied はまだ呼ばれていません)";
    OnCopiedLog.IsViewOnly = true;
}

void CopyFixed_OnCopied()
{
    OnCopiedLog.Value = "OnCopied が呼ばれました";
}

void ScriptCopy_OnClick()
{
    CopyInput.Copy();
}

void ServiceCopy_OnClick()
{
    if (Clipboard.Copy("Clipboard サービスから: " + Input.Value)) Toaster.Success("Clipboard.Copy でコピーしました");
    else Toaster.Error("Clipboard.Copy でコピーできませんでした");
}
