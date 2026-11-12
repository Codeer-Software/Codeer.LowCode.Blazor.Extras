## Design

押すと文字列をクリップボードにコピーするボタンです。
コピーする文字列は、同じモジュールの TextField (`SourceField`) の値か固定文字列 (`CopyText`) のどちらか一方から取ります。

### 動作

1. コピーする文字列を決める
   - `SourceField` が設定されている: その TextField の値そのまま (null は空文字)
   - `SourceField` が空: `CopyText`
2. ブラウザーの `navigator.clipboard.writeText` でクリップボードに書き込む
3. 成功したら `CopiedMessage` (空なら「コピーしました」) を通知して `OnCopied` を呼ぶ。失敗したらエラーを通知する

HTTPS でも localhost でもない環境や、クリック操作の外から呼んだ場合はブラウザーの制限でコピーできません。

コマンドや URL を見せてコピーさせたいときは、参照モードの (複数行の) TextField に入れて表示し、それをコピー元にする。
凝った表示 (MarkupStringField / LabelField など) から組み立てた文字列をコピーしたいときは、スクリプトで組み立てて `Clipboard.Copy(...)` に渡す。

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| Text | string | - | ボタンの文字。空なら「コピー」 |
| SourceField | string | △ | コピー元のフィールド名 (同じモジュールの TextField)。`CopyText` とどちらか一方 |
| CopyText | string | △ | コピーする固定文字列。`SourceField` とどちらか一方 |
| CopiedMessage | string | - | コピーできたときの通知。空なら「コピーしました」 |
| OnCopied | string | - | コピーできた後に呼ぶスクリプトの関数 |

`SourceField` と `CopyText` が両方空・両方設定のときはデザインチェックが指摘します (`ClipboardCopyButtonFieldDesign:1`)。
`SourceField` が TextField 以外のときも指摘します。

見た目は固定 (`btn btn-outline-secondary`)。見た目を選びたいときは本体の ButtonField + スクリプトの `Clipboard.Copy(...)` を使う。

### モジュール JSON 例

```json
{
  "SourceField": "TryPrompt",
  "Name": "CopyButton",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ClipboardCopyButtonFieldDesign"
}
```

## Script

### スクリプト API

| メソッド | 戻り値 | 説明 |
|---|---|---|
| `Copy()` | `bool` | デザインのコピー元をコピーする (ボタンを押したときと同じ) |

任意の文字列をコピーするときは、このフィールドではなくスクリプトの `Clipboard.Copy(string)` を使う (通知は出さないので結果を見て `Toaster` で知らせる)。

```csharp
void OtherButton_OnClick()
{
    CopyButton.Copy();
}
```

## CSS

### CSS カスタマイズ

Bootstrap のボタン (`btn btn-outline-secondary`) として、クリップボードのアイコン (`bi-clipboard`) と文字で描画されます。
`data-system="clipboard-copy"` 属性を持ちます。
