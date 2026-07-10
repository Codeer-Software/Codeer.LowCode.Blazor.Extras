外部 API に HTTP リクエストを送信する。ボディは `JsonObject` (動的プロパティ) で組み立てる。

```csharp
// GET
void FetchData_OnClick()
{
    var result = WebApiService.Get("/api/products");
    if (result.StatusCode != 200) return;
    foreach (var e in result.JsonObject)
    {
        var row = new DataItem();
        row.Name.Value = e.Name;
        DataList.AddRow(row);
    }
}

// POST
void Register_OnClick()
{
    var body = new JsonObject();
    body.Name = "新商品";
    body.Price = 1000;
    var result = WebApiService.Post("/api/products", body);
    if (result.StatusCode == 200)
    {
        Toaster.Success("登録しました");
    }
}
```
