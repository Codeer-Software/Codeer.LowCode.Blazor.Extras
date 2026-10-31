## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.RichTextFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

書式付きテキスト（太字・箇条書き・リンク等）をリッチテキストエディタで入力し、HTML 文字列として DB カラムに保存する値フィールド。`ValueFieldDesignBase` を継承し、値の型は `string`（HTML マークアップ）。

> 表示専用で HTML を出すだけなら [MarkupStringField](MarkupStringField.md)（core）。ユーザーに書式付きで**入力**させたいときに RichTextField を使う。

### C# クラス定義 (真実の源)

このフィールドは外部ライブラリ `Codeer.LowCode.Blazor.Extras` で定義されている。`ValueFieldDesignBase` を継承する。

### プロパティ

> 共通プロパティ（Name, IgnoreModification, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。
> 値フィールド共通プロパティ（DisplayName, IsRequired, OnDataChanged）は [_FieldCommon.md](_FieldCommon.md) の「ValueFieldDesignBase」を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `DbColumn` | string | `""` | HTML 文字列を保存する DB カラム名。 |

### 必要な DB 構成

HTML 文字列はそれなりに長くなりうるため、長さ無制限のテキスト型カラムを用意する。

```sql
content TEXT NULL
```

### JSON例

```json
{
  "DbColumn": "content",
  "DisplayName": "本文",
  "IsRequired": false,
  "OnDataChanged": "",
  "Name": "Content",
  "IgnoreModification": false,
  "OnValidateInput": "",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.RichTextFieldDesign"
}
```

> 既定状態は [../../Defaults/RichTextFieldDesign.json](../../Defaults/RichTextFieldDesign.json) を参照。

### ランタイム動作

- 編集モードではリッチテキストエディタを表示し、入力結果を HTML 文字列として保持する。
- 表示するとき・エディタに入れるとき・貼り付けるときに HTML を消毒する。スクリプトが動くもの (script・イベント属性・javascript: の URL)・フォームの部品・class・位置や重なりを変える style (position・z-index 等) を落とす。文字の書式・色・揃え・表・画像・リンクは残る。
- `IsRequired: true` のとき値が空（または空白のみ）だとバリデーションエラーになる。

## Script

### プロパティ

| プロパティ | 型 | 読み書き | 説明 |
|---|---|---|---|
| `Value` | string | 読み書き | 入力された HTML 文字列。 |

> 共通スクリプトプロパティ（Color, BackgroundColor, IsEnabled, IsVisible, IsViewOnly 等）は [_FieldCommon.md](_FieldCommon.md) / [_ScriptApi.md](_ScriptApi.md) を参照。
