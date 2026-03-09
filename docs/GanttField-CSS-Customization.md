# GanttField CSS カスタマイズガイド

GanttFieldComponent の各要素にはCSSクラスが付与されています。
ローコード設定のCSSでこれらのクラスを指定することで、見た目を自由にカスタマイズできます。

## CSS クラス一覧

### コンテナ・ツールバー

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-container` | ガントチャート全体のルート要素 | `border-radius`, `border`, `background`, `font-family` |
| `.gantt-toolbar` | ツールバー（ナビゲーション + ビュー切替） | `background`, `border-bottom`, `padding` |
| `.gantt-toolbar-left` | ツールバー左側（ナビゲーション） | `gap` |
| `.gantt-toolbar-right` | ツールバー右側（追加ボタン + ビュー切替） | `gap` |
| `.gantt-toolbar-btn` | ツールバーのボタン共通 | `border-radius`, `border`, `color` |
| `.gantt-toolbar-btn-today` | 「今日」ボタン | `background`, `font-weight` |
| `.gantt-toolbar-btn-prev` | 前へボタン (◀) | `background` |
| `.gantt-toolbar-btn-next` | 次へボタン (▶) | `background` |
| `.gantt-toolbar-btn-add` | 追加ボタン | `background`, `color`, `border-color` |
| `.gantt-toolbar-title` | タイトル（期間表示） | `font-size`, `color` |
| `.gantt-view-switcher` | ビュー切替ボタングループ | `border-radius`, `border` |
| `.gantt-view-btn` | ビュー切替ボタン（日/週/月） | `background`, `color`, `border-radius` |
| `.gantt-view-btn.active` | 選択中のビューボタン | `background`, `color` |

### 左パネル（タスクラベル）

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-labels` | 左パネル全体 | `width`, `min-width`, `background`, `border-right` |
| `.gantt-label-header` | ラベルヘッダー（「タスク」見出し） | `background`, `font-size`, `color`, `border-bottom` |
| `.gantt-label-body` | ラベル一覧のスクロール領域 | — |
| `.gantt-label-row` | タスク名の行 | `padding`, `border-bottom`, `background` |
| `.gantt-label-row:hover` | ホバー時の行 | `background` |
| `.gantt-label-text` | タスク名テキスト | `font-size`, `color` |
| `.gantt-label-progress` | 進捗率テキスト（例: "50%"） | `font-size`, `color` |

### タイムライン

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-body` | タイムライン＋ラベルのコンテナ | — |
| `.gantt-timeline-scroll` | タイムラインのスクロール領域 | — |
| `.gantt-timeline-svg` | SVG本体 | — |

### SVGヘッダー

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-header-month-bg` | 月グループの背景 | `fill` |
| `.gantt-header-day-bg` | 日の背景（平日） | `fill` |
| `.gantt-header-weekend-bg` | 日の背景（週末） | `fill` |
| `.gantt-header-offhour-bg` | 時間の背景（営業時間外） | `fill` |
| `.gantt-header-month-text` | 月名テキスト | `font-size`, `font-weight`, `fill` |
| `.gantt-header-date-text` | 日付テキスト（週表示ヘッダー） | `font-size`, `fill` |
| `.gantt-header-day-text` | 日番号テキスト | `font-size`, `fill` |
| `.gantt-header-hour-text` | 時刻テキスト | `font-size`, `fill` |
| `.gantt-header-sep` | ヘッダー主罫線 | `stroke`, `stroke-width` |
| `.gantt-header-sep-light` | ヘッダー補助罫線 | `stroke`, `stroke-width` |

### SVGグリッド

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-grid-weekend` | 週末の列背景 | `fill` |
| `.gantt-grid-line` | 補助グリッド線 | `stroke`, `stroke-width` |
| `.gantt-grid-line-main` | 主グリッド線（週/日の区切り） | `stroke`, `stroke-width` |
| `.gantt-grid-row-line` | 行区切り線 | `stroke`, `stroke-width` |

### 本日ライン

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-today-line` | 本日を示す縦線 | `stroke`, `stroke-width`, `stroke-dasharray` |
| `.gantt-today-marker` | 本日マーカー（三角形） | `fill` |

### タスクバー

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-task-bar` | タスクバー本体 | `fill`, `rx`, `ry` |
| `.gantt-task-bar:hover` | ホバー時のタスクバー | `fill` |
| `.gantt-task-progress` | 進捗バー（タスクバー内の塗り） | `fill` |
| `.gantt-task-text` | タスクバー上のテキスト | `font-size`, `fill`, `font-weight` |
| `.gantt-resize-handle` | リサイズハンドル（左右端） | `fill`, `cursor` |
| `.gantt-resize-handle:hover` | ホバー時のリサイズハンドル | `fill` |

### 依存関係矢印

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-dep-line` | 依存関係の線 | `stroke`, `stroke-width`, `stroke-dasharray` |
| `.gantt-dep-selected` | 選択中の依存関係線 | `stroke`, `stroke-width` |
| `.gantt-dep-hit-area` | 依存関係のクリック領域（透明） | `stroke-width` |
| `.gantt-dep-linking-stub` | 依存関係リンク中の仮線 | `stroke`, `stroke-dasharray` |
| `.gantt-dep-linking-dot` | 依存関係リンク中の端点ドット | `fill` |

### コンテキストメニュー

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.gantt-context-menu` | コンテキストメニュー本体 | `background`, `border`, `border-radius`, `box-shadow` |
| `.gantt-context-menu-item` | メニュー項目 | `padding`, `font-size`, `color` |
| `.gantt-context-menu-item:hover` | ホバー時のメニュー項目 | `background` |

## カスタマイズ例

### 角を全て直角にする

```css
.gantt-container {
  border-radius: 0;
}
.gantt-view-switcher {
  border-radius: 0;
}
.gantt-toolbar-btn {
  border-radius: 0;
}
.gantt-context-menu {
  border-radius: 0;
}
```

### タスクバーの色を変更する

```css
.gantt-task-bar {
  fill: #34a853;
}
.gantt-task-bar:hover {
  fill: #2d8e47;
}
.gantt-task-progress {
  fill: #1e6b30;
}
```

### 週末の背景色を変更する

```css
.gantt-grid-weekend {
  fill: #fff3e0;
}
.gantt-header-weekend-bg {
  fill: #ffe0b2;
}
```

### 本日ラインの色を変更する

```css
.gantt-today-line {
  stroke: #1a73e8;
  stroke-width: 2;
}
.gantt-today-marker {
  fill: #1a73e8;
}
```

### 依存関係線を実線にする

```css
.gantt-dep-line {
  stroke-dasharray: none;
  stroke: #9e9e9e;
}
```

### グリッド線を消す

```css
.gantt-grid-line,
.gantt-grid-line-main {
  stroke: transparent;
}
.gantt-grid-row-line {
  stroke: transparent;
}
```

### 左パネルの幅を変更する

```css
.gantt-labels {
  width: 300px;
  min-width: 300px;
}
```

### 背景色を変更する

```css
.gantt-container {
  background: #f5f5f5;
}
.gantt-toolbar {
  background: #e0e0e0;
}
.gantt-labels {
  background: #fafafa;
}
.gantt-label-header {
  background: #eeeeee;
}
```

> **Note:** SVG要素のスタイルは `fill` / `stroke` プロパティで変更します（`background` / `border` ではなく）。
