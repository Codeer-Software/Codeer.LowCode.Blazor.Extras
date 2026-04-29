# ColorPickerField CSS カスタマイズガイド

ColorPickerFieldComponent の各要素には CSS クラスが付与されています。ローコード設定の CSS でこれらのクラスを指定することで、見た目を自由にカスタマイズできます。

## CSS クラス一覧

| クラス名 | 要素 | 主なカスタマイズ対象 |
|---|---|---|
| `.colorpicker-view` | 閲覧モードのルート要素 (色チップ + HEX) | `padding`, `gap`, `align-items` |
| `.colorpicker-edit` | 編集モードのルート要素 (label) | `gap`, `align-items` |
| `.colorpicker-input` | カラーピッカー本体 (`<input type="color">`) | `width`, `height`, `border-radius`, `box-shadow`, `border` |
| `.colorpicker-input::-webkit-color-swatch` | カラーピッカー内のスウォッチ (Chromium 系) | `width`, `height`, `border-radius`, `border` |
| `.colorpicker-tip` | 閲覧モードの色チップ | `width`, `height`, `border-radius`, `border` |
| `.colorpicker-text` | HEX コードを表示するテキスト | `font-size`, `color`, `background`, `padding` |

## カスタマイズ例

### 大きめのカラーピッカーにする

```css
.colorpicker-input,
.colorpicker-tip {
  width: 36px;
  height: 36px;
  border-radius: 50%;
}
.colorpicker-input::-webkit-color-swatch {
  width: 36px;
  height: 36px;
  border-radius: 50%;
}
```

### HEX テキストを等幅フォントにする

```css
.colorpicker-text {
  font-family: "Consolas", "Menlo", monospace;
  font-size: 0.9rem;
  letter-spacing: 0.05em;
}
```

### 色チップを四角に変更する

```css
.colorpicker-tip {
  border-radius: 2px;
  border: 2px solid #333;
}
```

### 閲覧モードの余白を詰める

```css
.colorpicker-view {
  padding: 0;
  gap: 4px;
}
```

### HEX テキストを非表示にする (色チップのみ表示)

```css
.colorpicker-text {
  display: none;
}
```

### ホバー時にカラーピッカーを浮かす

```css
.colorpicker-input {
  transition: transform 0.1s ease, box-shadow 0.1s ease;
}
.colorpicker-input:hover {
  transform: scale(1.1);
  box-shadow: 0 2px 8px rgb(64 64 64 / 30%);
}
```

## 注意事項

- **inline style の優先順位**: フィールドのデザイナー設定 (`Color` / `BackgroundColor`) で指定された色は、`.colorpicker-text` 要素に CSS 変数 (`--colorpicker-foreground` / `--colorpicker-background`) として適用されます。CSS で統一したい場合はデザイナー側を空にしてください。
- **ブラウザ依存の見た目**: `<input type="color">` のスウォッチ部分は OS / ブラウザにより内部構造が異なります。Chromium 系では `::-webkit-color-swatch`、Firefox では `::-moz-color-swatch` 疑似要素でカスタマイズします。
