# ColorPickerField - カラーピッカー

色 (HEX文字列) を選択・保存できる値フィールドです。HTML5 ネイティブのカラーピッカー (`<input type="color">`) を使用し、選択した色を `#rrggbb` 形式の文字列として DB に保存します。

## 機能

- **カラーピッカー**: クリックで OS / ブラウザ標準のカラーピッカーが開く
- **デフォルト色**: 値が未設定のときに表示する初期色を指定可能
- **HEX 表示**: 選択中の色コードを横に表示
- **必須入力チェック**: `IsRequired` を有効にすると未入力をエラーにできる
- **閲覧モード**: 色チップと HEX コードのみを表示

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| DbColumn | string | ○ | 色文字列を保存する DB カラム名 |
| Default | string | - | 値が空のときに表示する初期色 (例: `#000000`)。デフォルトは `#000000` |
| IsRequired | bool | - | true にすると未入力をバリデーションエラー |
| DisplayName | string | - | フィールドの表示名 (共通) |
| Color | string | - | 文字色 (共通、inline style として適用) |
| BackgroundColor | string | - | 背景色 (共通、inline style として適用) |
| OnDataChanged | script | - | 値変更時に呼ばれるスクリプトイベント |

## 必要な DB 構成

`#rrggbb` 形式の文字列 (7 文字) を格納します。`NVARCHAR(7)` 〜 `NVARCHAR(16)` 程度の文字列カラムを推奨します。

```sql
ColorCode NVARCHAR(16) NULL
```

## 表示モード

- **編集モード**: HTML5 ネイティブカラーピッカー + 選択中の HEX コード文字列
- **閲覧モード**: 色チップ (24px の角丸スウォッチ) + HEX コード文字列

値が空の場合は `Default` プロパティで指定した色を表示しますが、フィールド値自体は空のまま保持されます (`IsRequired = true` の場合は空のため検証エラー)。

## スクリプト API

| メンバー | 種別 | 説明 |
|---|---|---|
| Value | プロパティ (string?) | 選択中の色文字列 (`#rrggbb`)。取得・設定可能 |

### 使用例

```javascript
// 別フィールドの色を取得して背景色として使う
async function OnSomeChange() {
  const color = ColorField1.Value;
  if (color) {
    BackgroundField.Value = color;
  }
}
```

## バリデーション

- `IsRequired = true` の場合、`Value` が空または空白のみのときに「入力エラー」を表示します。
- HEX 形式の妥当性チェックは行いません (HTML5 カラーピッカーが常に正しい形式で値を返すため)。

## CSS カスタマイズ

カラーピッカーの見た目は CSS クラスで自由にカスタマイズできます。全 CSS クラス一覧とカスタマイズ例は **[ColorPickerField CSS カスタマイズガイド](ColorPickerField-CSS-Customization.md)** を参照してください。
