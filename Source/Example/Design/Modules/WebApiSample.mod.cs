void Search_OnClick()
{
    var args = new JsonObject();
    args.Id = UserId.Value;
    var ret = UserApi.Execute(args);
    if (!ret.IsSuccess)
    {
        Toaster.Error("取得できませんでした: " + ret.ErrorMessage);
        return;
    }
    UserName.Value = ret.JsonObject.Name;
}

void PostApi_OnCompleted(WebApiResult result)
{
    if (result.IsSuccess)
    {
        Result.Value = "投稿しました";
    }
    else
    {
        Result.Value = "投稿できませんでした";
    }
}
