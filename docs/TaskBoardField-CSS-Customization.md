# TaskBoardField CSS カスタマイズガイド

TaskBoardFieldComponent の各要素にはCSSクラスが付与されています。
ローコード設定のCSSでこれらのクラスを指定することで、見た目を自由にカスタマイズできます。

## CSS クラス一覧

### コンテナ・レイアウト

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.taskboard-container` | タスクボード全体のルート要素 | `font-family`, `font-size`, `background` |
| `.taskboard-columns` | カラム群のコンテナ（横並び） | `gap`, `padding-bottom`, `min-height` |

### カラム

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.taskboard-column` | 各ステータスカラム | `min-width`, `max-width`, `background`, `border-radius` |
| `.taskboard-column-header` | カラムヘッダー | `background`, `border-bottom`, `padding`, `border-radius` |
| `.taskboard-column-title` | ステータス名テキスト | `font-size`, `font-weight`, `color` |
| `.taskboard-column-count` | カード件数バッジ | `font-size`, `color`, `background`, `border-radius`, `padding` |
| `.taskboard-column-body` | カラム本体（カード一覧） | `padding`, `gap`, `min-height` |

### 追加ボタン

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.taskboard-add-btn` | カラムヘッダーの「+」ボタン | `font-size`, `width`, `height`, `border-radius`, `color`, `background` |
| `.taskboard-add-btn:hover` | ホバー時 | `background`, `opacity` |

### カード

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.taskboard-card` | タスクカード | `background`, `border-radius`, `box-shadow`, `border` |
| `.taskboard-card:hover` | ホバー時のカード | `box-shadow`, `background` |
| `.taskboard-card:active` | ドラッグ中のカーソル | `cursor` |
| `.taskboard-card.dragging` | ドラッグ中のカード（元位置） | `opacity` |

### ドラッグ&ドロップ

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.taskboard-empty-drop` | 空きエリアのドロップゾーン | `min-height`, `border`, `border-radius` |
| `.taskboard-empty-drop.drag-over` | ドラッグオーバー時の空きエリア | `border-color`, `background` |
| `.taskboard-card-drop-before` | カード上半分のドロップゾーン（内部） | — |
| `.taskboard-card-drop-after` | カード下半分のドロップゾーン（内部） | — |

## ステータスごとの色設定

各ステータスの `Color`（文字色）と `BackgroundColor`（背景色）はデザイナーで設定できます。
設定値はカラムヘッダーの inline style として適用されるため、CSS クラスよりも優先されます。

- **Color** — ヘッダーの文字色（タイトル、件数バッジ、ボタンに影響）
- **BackgroundColor** — ヘッダーの背景色・下線色
- 両方とも未設定の場合はCSSクラスのデフォルト色（`#e2e4e9`）が使われます

## カスタマイズ例

### カラムの背景色を変更する

```css
.taskboard-column {
  background: #e8eaf6;
}
.taskboard-column-header {
  background: #c5cae9;
  border-bottom-color: #9fa8da;
}
```

### カードのスタイルを変更する

```css
.taskboard-card {
  background: #fffde7;
  border: 1px solid #f9a825;
  border-radius: 4px;
  box-shadow: none;
}
.taskboard-card:hover {
  box-shadow: 0 2px 8px rgba(249, 168, 37, 0.3);
}
```

### 角を全て直角にする

```css
.taskboard-column {
  border-radius: 0;
}
.taskboard-column-header {
  border-radius: 0;
}
.taskboard-card {
  border-radius: 0;
}
.taskboard-empty-drop {
  border-radius: 0;
}
```

### カラム幅を変更する

```css
.taskboard-column {
  min-width: 200px;
  max-width: 500px;
}
```

### カラム間の余白を変更する

```css
.taskboard-columns {
  gap: 0.5rem;
}
```

### ドロップゾーンの色を変更する

```css
.taskboard-empty-drop.drag-over {
  border-color: #e91e63;
  background: #fce4ec;
}
```

### 件数バッジを非表示にする

```css
.taskboard-column-count {
  display: none;
}
```

### 追加ボタンを目立たせる

```css
.taskboard-add-btn {
  background: #1a73e8;
  color: #fff;
  opacity: 1;
  font-size: 16px;
}
.taskboard-add-btn:hover {
  background: #1557b0;
}
```

### フォントを変更する

```css
.taskboard-container {
  font-family: "游ゴシック", "Yu Gothic", sans-serif;
}
```

> **Note:** ステータスごとの `Color` / `BackgroundColor` がデザイナーで設定されている場合、inline style がCSSクラスより優先されます。CSSで全カラムの色を統一したい場合は、デザイナー側の色設定を空にしてください。
