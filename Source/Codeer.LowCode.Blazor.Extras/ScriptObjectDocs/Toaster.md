画面にトースト (一時通知) を表示する。処理結果の通知に使う (入力を求めるなら `MessageBox`)。

- `Success` は表示中のトーストを消してから表示する (成功時に古いエラー通知が残らない)
- `Error` は見逃し防止のため長め (30 秒) に表示される
- **`Toaster.Warning` は存在しない**。警告は `Toaster.Warn` を使う

```csharp
void SaveButton_OnClick()
{
    var ret = this.Submit();
    if (ret != true)
    {
        Toaster.Error("保存に失敗しました");
        return;
    }
    Toaster.Success("保存しました");
}
```
