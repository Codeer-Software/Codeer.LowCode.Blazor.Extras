void DetailLayoutDesign_OnAfterInitialization()
{
    Tags.Value = "展示会2026, DXPO";
    TagsView.IsViewOnly = true;
}

void Copy_OnClick()
{
    TagsView.Value = Tags.Value;
    Result.Text = Tags.Tags.Count + " 件 / DXPO " + (Tags.HasTag("DXPO") ? "あり" : "なし");
}
