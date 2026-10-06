void DetailLayoutDesign_OnAfterInitialization()
{
    TagsView.IsViewOnly = true;
}

void Copy_OnClick()
{
    TagsView.SetTags(Tags.Tags);
    Result.Text = Tags.Tags.Count + " 件 / DXPO " + (Tags.HasTag("DXPO") ? "あり" : "なし");
}
