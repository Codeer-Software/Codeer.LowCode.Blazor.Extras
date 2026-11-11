## Design

押すと文字列をクリップボードにコピーするボタンです。
コピーする文字列は、同じモジュールの別のフィールド (`SourceField`) か固定文字列 (`CopyText`) のどちらか一方から取ります。

### 動作

1. コピーする文字列を決める
   - `SourceField` が MarkupStringField: HTML をテキストにする (`RawHtml` が優先。空なら `ResourcePath` のリソースの HTML)
   - `SourceField` が LabelField: 表示文字列 (`IsHtml` のときはテキストにする)
   - `SourceField` が TextField: 入力中の値
   - `SourceField` が空: `CopyText`
2. ブラウザーの `navigator.clipboard.writeText` でクリップボードに書き込む
3. 成功したら `CopiedMessage` (空なら「コピーしました」) を通知して `OnCopied` を呼ぶ。失敗したらエラーを通知する

HTTPS でも localhost でもない環境や、クリック操作の外から呼んだ場合はブラウザーの制限でコピーできません。

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| Text | string | - | ボタンの文字。空なら「コピー」 |
| Variant | enum | - | ボタンの見た目 (Bootstrap のボタンの種類)。既定は `OutlineSecondary` |
| SourceField | string | △ | コピー元のフィールド名 (MarkupStringField / LabelField / TextField)。`CopyText` とどちらか一方 |
| CopyText | string | △ | コピーする固定文字列。`SourceField` とどちらか一方 |
| CopiedMessage | string | - | コピーできたときの通知。空なら「コピーしました」 |
| OnCopied | string | - | コピーできた後に呼ぶスクリプトの関数 |

`SourceField` と `CopyText` が両方空・両方設定のときはデザインチェックが指摘します (`ClipboardCopyButtonFieldDesign:1`)。

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
| `CopyText(string text)` | `bool` | 指定した文字列をコピーする |

```csharp
void OtherButton_OnClick()
{
    CopyButton.CopyText($"注文番号: {OrderNo.Value}");
}
```

## CSS

### CSS カスタマイズ

Bootstrap のボタン (`Variant` に応じた `btn btn-...` クラス) として、クリップボードのアイコン (`bi-clipboard`) と文字で描画されます。
`data-system="clipboard-copy"` 属性を持ちます。
