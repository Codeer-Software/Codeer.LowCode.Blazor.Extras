文字列をクリップボードにコピーする。`Clipboard.Copy(string)` は成功なら `true`、コピーできなかったら `false` を返す。

- 通知は出さない。結果に応じて `Toaster` などで知らせる
- ブラウザーの制限により、HTTPS か localhost で、ボタンのクリックなどユーザー操作の中から呼んだときだけ成功する
- 画面上の文字列をボタン 1 つでコピーするだけなら、スクリプト不要の ClipboardCopyButtonField が使える

```csharp
void CopyButton_OnClick()
{
    if (Clipboard.Copy($"注文番号: {OrderNo.Value}")) Toaster.Success("コピーしました");
    else Toaster.Error("コピーできませんでした");
}
```
