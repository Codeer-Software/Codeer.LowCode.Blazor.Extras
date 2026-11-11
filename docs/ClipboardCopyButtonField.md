# ClipboardCopyButtonField - クリップボードコピーボタン

押すと文字列をクリップボードにコピーするボタンです。
コピーする文字列は、同じモジュールの別のフィールド (コピー元フィールド) か、デザインに書いた固定文字列から取ります。
「コマンドや URL を画面に表示して、その右にコピーボタンを置く」ような画面を、スクリプトなしで作れます。

## 機能

- ボタン 1 つで文字列をクリップボードにコピーし、「コピーしました」と通知する
- コピー元にできるのは MarkupStringField / LabelField / TextField
  - MarkupStringField は HTML をテキストにしてコピーする (`RawHtml` が優先。空なら `ResourcePath` のリソースの HTML)
  - LabelField は表示文字列 (`IsHtml` のときはテキストにする)
  - TextField は入力中の値
- コピーできた後にスクリプトイベント (`OnCopied`) を呼べる
- スクリプトから `Copy()` でもコピーできる (任意の文字列はスクリプトの `Clipboard.Copy("...")`)

## デザイナー設定プロパティ

| プロパティ | デザイナ表示名 | 型 | 必須 | 説明 |
|---|---|---|---|---|
| Text | ボタンの文字 | string | - | ボタンの文字。空なら「コピー」 |
| Variant | ボタンの種類 | enum | - | ボタンの見た目 (Bootstrap のボタンの種類)。既定は `OutlineSecondary` |
| SourceField | コピー元フィールド | string | △ | コピー元のフィールド名 (同じモジュールの MarkupStringField / LabelField / TextField) |
| CopyText | コピーする文字列 | string | △ | コピーする固定文字列 |
| CopiedMessage | コピー後のメッセージ | string | - | コピーできたときの通知。空なら「コピーしました」 |
| OnCopied | コピー後イベント | string | - | コピーできた後に呼ぶスクリプトの関数 |

`SourceField` と `CopyText` は**どちらか一方だけ**を設定します。両方空・両方設定はデザインチェックが指摘します (`ClipboardCopyButtonFieldDesign:1`)。
`SourceField` に存在しないフィールドや、コピー元にできない種類のフィールドを指定した場合も指摘されます。

## モジュール JSON 例

表示用の MarkupStringField と並べて置く例です (グリッドレイアウトで同じ行に配置します)。

```json
{
  "RawHtml": "<code>このURLを見て指示に従って https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Starter</code>",
  "Name": "TryPrompt",
  "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.MarkupStringFieldDesign"
},
{
  "SourceField": "TryPrompt",
  "Name": "CopyButton",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ClipboardCopyButtonFieldDesign"
}
```

## HTML をテキストにするときの規則

MarkupStringField (と `IsHtml` の LabelField) は、HTML のタグを取り除いてからコピーします。

- `<br>` と `</p>` `</div>` `</li>` `</tr>` `</h1>`〜`</h6>` `</table>` は改行になる
- `&amp;` などの文字参照は元の文字に戻る
- 連続する空白は 1 つにまとめ、前後の空白と改行は取り除く

## スクリプト API

| メソッド | 戻り値 | 説明 |
|---|---|---|
| `Copy()` | `bool` | デザインのコピー元 (コピー元フィールド / コピーする文字列) をコピーする。ボタンを押したときと同じ |

コピーできたら通知を出して `OnCopied` を呼び、`true` を返します。コピーできなかったときはエラーを通知して `false` を返します。

```csharp
void OtherButton_OnClick()
{
    CopyButton.Copy();
}
```

任意の文字列をコピーしたいときや、ボタンを置かずにスクリプトだけでコピーしたいときは、スクリプトの `Clipboard` サービスを使います (`ExtrasClientInitializer.Initialize` で登録済み)。
`Clipboard.Copy(string)` は成功なら `true` を返し、通知は出しません。書き込みの仕組みはこのフィールドと同じです。

```csharp
void CopyButton_OnClick()
{
    if (Clipboard.Copy($"注文番号: {OrderNo.Value}")) Toaster.Success("コピーしました");
    else Toaster.Error("コピーできませんでした");
}
```

## 使える環境の注意

クリップボードへの書き込みにはブラウザーの `navigator.clipboard.writeText` を使います。ブラウザーの制限により、次の場合はコピーできずにエラーを通知します。

- HTTPS でも localhost でもない (http の IP アドレスなど) で開いている
- ユーザーのクリック操作の外から呼んだ (スクリプトの `Copy()` / `Clipboard.Copy()` は、ボタンのクリックなどの操作の中から呼んでください)
- 埋め込み先 (iframe や WebView) でクリップボードの書き込みが許可されていない

## CSS カスタマイズ

Bootstrap のボタン (`Variant` に応じた `btn btn-...` クラス) として、クリップボードのアイコンと文字で描画されます。
`data-system="clipboard-copy"` 属性を持ちます。デザインモードでは押せない状態で表示されます。
